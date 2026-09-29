using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A bot running for a component test: your service registrations and the library's real hosting, with
/// <see cref="FakeBotApi"/> standing in for Telegram.
/// </summary>
public sealed class TelegramTestHost : IAsyncDisposable
{
    private readonly IRunningBot _bot;
    private readonly ErrorLog _errors;

    private TelegramTestHost(IRunningBot bot, FakeBotApi api, ErrorLog errors)
    {
        _bot = bot;
        _errors = errors;
        Api = api;
    }

    /// <summary>The fake Telegram the bot talks to: every call it made, and the chats as they now stand.</summary>
    public FakeBotApi Api { get; }

    /// <summary>The bot's root services, for arranging state or checking it afterwards.</summary>
    public IServiceProvider Services => _bot.Services;

    internal TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the bot the way long polling runs it in production: <paramref name="configureServices"/> registers the
    /// application as its own composition root does — including <c>AddTelegramLongPollingReceiving</c> — and the
    /// started host's real polling loop pulls updates from the fake.
    /// </summary>
    /// <param name="configureServices">Registers the bot, as its composition root does.</param>
    /// <param name="api">
    /// The fake to run against, already arranged — to fail a call the bot makes as it starts, say. A new one when
    /// <c>null</c>.
    /// </param>
    /// <param name="token">Stops waiting for the bot to start.</param>
    /// <remarks>
    /// Two registrations are swapped: the bot client, for one talking to <see cref="Api"/>, so the token in your
    /// configuration is never used; and the error handler, so a command's exception fails the test instead of reaching
    /// your <c>ITelegramErrorHandler</c>. Every hosted service starts, as in production, and the host has finished
    /// starting when this returns — so what the bot does as it starts, such as publishing its command menu, can be
    /// checked straight away. The container is validated on build, so a registration that cannot be resolved fails
    /// here rather than in the middle of a test.
    /// </remarks>
    public static async Task<TelegramTestHost> ForLongPollingAsync(
        Action<IServiceCollection> configureServices,
        FakeBotApi? api = null,
        CancellationToken token = default
    )
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();

        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        configureServices(builder.Services);
        TalkToTheFake(builder.Services, api, errors);
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            )
        );

        var host = builder.Build();
        await host.StartAsync(token);

        return new TelegramTestHost(new PollingBot(host, api), api, errors);
    }

    /// <summary>
    /// Starts the bot the way a webhook runs it in production: the ASP.NET Core app whose entry point is
    /// <typeparamref name="TEntryPoint"/> — usually <c>Program</c> — starts in memory through
    /// <see cref="WebApplicationFactory{TEntryPoint}"/>, sets its webhook with the fake as it starts, and every update
    /// is posted to that webhook as Telegram would post it.
    /// </summary>
    /// <param name="configureWebHost">
    /// Adjusts the app for the test, as <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/> does —
    /// typically to supply the webhook's configuration with <c>ConfigureAppConfiguration</c>.
    /// </param>
    /// <param name="api">
    /// The fake to run against, already arranged — to fail a call the bot makes as it starts, say. A new one when
    /// <c>null</c>.
    /// </param>
    /// <remarks>
    /// Two registrations are swapped, as for long polling: the bot client, so the token in your configuration is never
    /// used, and the error handler, so a command's exception fails the test. The app runs in the Development
    /// environment and has finished starting when this returns, so the webhook it set is in
    /// <see cref="FakeBotApi.WebhookUrl"/>. Updates are posted to that very URL on the in-memory server — its host,
    /// scheme and path — with the secret token header when the bot set one.
    /// </remarks>
    public static async Task<TelegramTestHost> ForWebhookAsync<TEntryPoint>(
        Action<IWebHostBuilder>? configureWebHost = null,
        FakeBotApi? api = null
    )
        where TEntryPoint : class
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();

        // Disposing the factory disposes the one derived from it, which runs the app.
        var factory = new WebApplicationFactory<TEntryPoint>();
        var app = factory.WithWebHostBuilder(web =>
        {
            configureWebHost?.Invoke(web);
            web.ConfigureTestServices(services => TalkToTheFake(services, api, errors));
        });

        try
        {
            // Creating the client starts the app, and with it every hosted service — the one setting the webhook too.
            // Like Telegram, it follows no redirect and keeps no cookie, and it leaves timing out to UpdateTimeout.
            var client = await Task.Run(() =>
                app.CreateClient(
                    new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }
                )
            );
            client.Timeout = Timeout.InfiniteTimeSpan;

            return new TelegramTestHost(new WebhookBot(factory, app.Services, client, api), api, errors);
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// <paramref name="firstName"/>, talking to the bot in a private chat. Asking for the same name again gives the
    /// same person and chat.
    /// </summary>
    /// <remarks>
    /// A name is a person throughout the test: the same name as a <see cref="TestChat.Member"/> of a group is the
    /// same Telegram user, so the bot sees one id in both chats.
    /// </remarks>
    public TestUser PrivateChat(string firstName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        var person = Api.Person(firstName);
        var chat = new TestChat(this, Api.PrivateChatWith(person), firstName, isGroup: false);
        return new TestUser(this, person, chat);
    }

    /// <summary>
    /// The group called <paramref name="title"/>, with the bot in it. Asking for the same title again gives the same
    /// group; add people to it with <see cref="TestChat.Member"/>.
    /// </summary>
    public TestChat GroupChat(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new TestChat(this, Api.Group(title), title, isGroup: true);
    }

    /// <summary>
    /// Delivers <paramref name="update"/> to the bot and returns once the bot has finished handling it, rethrowing
    /// whatever a command threw so a crash fails the test.
    /// </summary>
    /// <remarks>
    /// For updates the test chats do not cover. A message sent this way is not added to any <see cref="TestChat"/>.
    /// </remarks>
    /// <exception cref="TimeoutException">The bot never finished the update.</exception>
    /// <exception cref="InvalidOperationException">
    /// Webhook mode only: the bot set no webhook to post the update to, or did not answer it with a success.
    /// </exception>
    public Task SendAsync(Update update, CancellationToken token = default) =>
        DeliverAsync(JsonSerializer.SerializeToNode(update, JsonBotAPI.Options)!.AsObject(), token);

    internal async Task DeliverAsync(JsonObject update, CancellationToken token)
    {
        await _bot.DeliverAsync(update, UpdateTimeout, token);
        _errors.ThrowIfAny();
    }

    public ValueTask DisposeAsync() => _bot.DisposeAsync();

    // Swaps the bot client, for one talking to the fake, and the error handler, for one recording the errors commands
    // throw so the test sees them.
    private static void TalkToTheFake(IServiceCollection services, FakeBotApi api, ErrorLog errors)
    {
        services.Replace(ServiceDescriptor.Singleton(new TelegramBotClientAccessor(api.CreateClient())));
        services.Replace(ServiceDescriptor.Scoped<ITelegramErrorHandler>(_ => new RecordingErrorHandler(errors)));
    }

    // How updates reach the running bot, which differs between its two ways of receiving them.
    private interface IRunningBot : IAsyncDisposable
    {
        IServiceProvider Services { get; }

        Task DeliverAsync(JsonObject update, TimeSpan timeout, CancellationToken token);
    }

    // The polling loop takes the update from getUpdates, and asks for the next one only once it has finished with it.
    private sealed class PollingBot(IHost host, FakeBotApi api) : IRunningBot
    {
        public IServiceProvider Services => host.Services;

        public async Task DeliverAsync(JsonObject update, TimeSpan timeout, CancellationToken token)
        {
            var updateId = api.Enqueue(update);

            try
            {
                await api.HandledAsync(updateId).WaitAsync(timeout, token);
            }
            catch (TimeoutException)
            {
                var cause = api.WebhookUrl is { } webhook
                    ? $"Telegram still has a webhook for the bot, {webhook}, so it refuses every getUpdates with 409. "
                        + "Did deleting it fail? Api.Calls shows what the bot asked Telegram."
                    : "Is long polling registered, for example with AddTelegramLongPollingReceiving?";

                throw new TimeoutException(
                    $"The bot did not finish update {updateId} within {timeout.TotalSeconds:0} seconds. {cause}"
                );
            }
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();

            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    // The update is posted to the webhook the bot set, and the endpoint answers once it has finished with it.
    private sealed class WebhookBot(
        IAsyncDisposable factory,
        IServiceProvider services,
        HttpClient client,
        FakeBotApi api
    ) : IRunningBot
    {
        private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

        private static readonly TimeSpan AbandonedGrace = TimeSpan.FromSeconds(5);

        // Updates the test stopped waiting for, which the bot may still be working on.
        private readonly List<Task> _abandoned = [];

        public IServiceProvider Services => services;

        public async Task DeliverAsync(JsonObject update, TimeSpan timeout, CancellationToken token)
        {
            var webhook =
                api.Webhook()
                ?? throw new InvalidOperationException(
                    "The bot has set no webhook, so there is nowhere to post the update. Is webhook receiving "
                        + "registered, for example with AddTelegramWebhookReceiving and UseTelegramWebhook, and did "
                        + "its setWebhook succeed? Api.Calls shows what the bot asked Telegram."
                );

            // Posted to the webhook URL itself, so the app sees the host and scheme Telegram would call it on.
            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(
                    api.StampForWebhook(update).ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                ),
            };

            if (webhook.SecretToken is { } secretToken)
            {
                request.Headers.Add(SecretTokenHeader, secretToken);
            }

            // Like Telegram, the test only stops waiting when it gives up: aborting the request would cancel the bot
            // in the middle of the update, and the error that leaves behind would fail the next one.
            var sending = client.SendAsync(request, CancellationToken.None);
            HttpResponseMessage response;
            try
            {
                response = await sending.WaitAsync(timeout, token);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                var cleanup = sending.ContinueWith(
                    sent =>
                    {
                        if (sent.IsCompletedSuccessfully)
                        {
                            sent.Result.Dispose();
                        }
                        else
                        {
                            // Observed, so a failure after the test moved on is not reported as unobserved.
                            _ = sent.Exception;
                        }

                        request.Dispose();
                    },
                    TaskScheduler.Default
                );

                lock (_abandoned)
                {
                    _abandoned.Add(cleanup);
                }

                if (exception is TimeoutException)
                {
                    throw new TimeoutException(
                        $"The bot did not answer the update posted to {webhook.Url} within "
                            + $"{timeout.TotalSeconds:0} seconds."
                    );
                }

                throw;
            }

            using (request)
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(await FailedDeliveryAsync(webhook.Url, response));
                }
            }
        }

        // Says why a delivery failed: a missing endpoint for 404 and 405, where a redirect led, and otherwise what the
        // app answered.
        private static async Task<string> FailedDeliveryAsync(string url, HttpResponseMessage response)
        {
            const int longestBody = 1000;

            var failure =
                $"The bot answered the update posted to {url} with {(int)response.StatusCode} "
                + $"{response.ReasonPhrase}, which Telegram takes as a failed delivery.";

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return failure + " Is the update endpoint mapped there, for example with UseTelegramWebhook?";
            }

            if (response.Headers.Location is { } location)
            {
                return $"{failure} It redirects to {location}, and Telegram follows no redirect.";
            }

            var body = await response.Content.ReadAsStringAsync();
            return body.Length == 0 ? failure
                : body.Length <= longestBody ? $"{failure} It said:\n{body}"
                : $"{failure} It said:\n{body[..longestBody]}…";
        }

        // Disposing the client cancels the updates still in flight; they are let finish before the app goes, so none
        // runs on against a disposed container.
        public async ValueTask DisposeAsync()
        {
            client.Dispose();

            Task[] abandoned;
            lock (_abandoned)
            {
                abandoned = [.. _abandoned];
            }

            await Task.WhenAll(abandoned)
                .WaitAsync(AbandonedGrace)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await factory.DisposeAsync();
        }
    }

    private sealed class ErrorLog
    {
        private readonly List<Exception> _errors = [];

        public void Add(Exception exception)
        {
            lock (_errors)
            {
                _errors.Add(exception);
            }
        }

        public void ThrowIfAny()
        {
            Exception? first;
            lock (_errors)
            {
                first = _errors.FirstOrDefault();
                _errors.Clear();
            }

            if (first is not null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }
    }

    private sealed class RecordingErrorHandler(ErrorLog errors) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            errors.Add(telegramError.Exception);
            return Task.CompletedTask;
        }
    }
}
