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

/// <summary>Your bot, running on its real hosting against <see cref="FakeBotApi"/> instead of Telegram.</summary>
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

    /// <summary>The fake Telegram the bot talks to.</summary>
    public FakeBotApi Api { get; }

    /// <summary>The bot's root services.</summary>
    public IServiceProvider Services => _bot.Services;

    internal TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts a long-polling bot: <paramref name="configureServices"/> registers it as its composition root does, and its
    /// real polling loop pulls updates from the fake.
    /// </summary>
    /// <param name="configureServices">The bot's registrations, including <c>AddTelegramLongPollingReceiving</c>.</param>
    /// <param name="api">A pre-arranged fake, e.g. to fail startup calls; a new one when <c>null</c>.</param>
    /// <param name="token">Stops waiting for the bot to start.</param>
    /// <remarks>
    /// Only the bot client (the token is never used) and the error handler (errors fail the test) are swapped. Returns
    /// once the host has started, so startup work such as the command menu can be checked right away. The container is
    /// validated on build.
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
    /// Starts a webhook bot: its ASP.NET Core app (<typeparamref name="TEntryPoint"/>, usually <c>Program</c>) runs in
    /// memory via <see cref="WebApplicationFactory{TEntryPoint}"/>, and each update is posted to the webhook it set.
    /// </summary>
    /// <param name="configureWebHost">Test tweaks, typically the webhook configuration via <c>ConfigureAppConfiguration</c>.</param>
    /// <param name="api">A pre-arranged fake, e.g. to fail startup calls; a new one when <c>null</c>.</param>
    /// <remarks>
    /// Swaps the same two registrations as long polling and runs in Development. Updates go to the exact webhook URL
    /// (host, scheme, path) with the secret token header, and no redirects or cookies, as from Telegram.
    /// </remarks>
    public static async Task<TelegramTestHost> ForWebhookAsync<TEntryPoint>(
        Action<IWebHostBuilder>? configureWebHost = null,
        FakeBotApi? api = null
    )
        where TEntryPoint : class
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();

        var factory = new WebApplicationFactory<TEntryPoint>();
        var app = factory.WithWebHostBuilder(web =>
        {
            configureWebHost?.Invoke(web);
            web.ConfigureTestServices(services => TalkToTheFake(services, api, errors));
        });

        try
        {
            // Creating the client starts the app; UpdateTimeout, not HttpClient.Timeout, bounds each update.
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
    /// <paramref name="firstName"/> in a private chat with the bot. A name is one Telegram user throughout the test,
    /// including as a <see cref="TestChat.Member"/> of a group.
    /// </summary>
    public TestUser PrivateChat(string firstName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        var person = Api.Person(firstName);
        var chat = new TestChat(this, Api.PrivateChatWith(person), firstName, isGroup: false);
        return new TestUser(this, person, chat);
    }

    /// <summary>The group <paramref name="title"/> with the bot in it; add people with <see cref="TestChat.Member"/>.</summary>
    public TestChat GroupChat(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new TestChat(this, Api.Group(title), title, isGroup: true);
    }

    /// <summary>
    /// Delivers a raw <paramref name="update"/> (it is not added to any <see cref="TestChat"/>) and returns once it is
    /// handled, rethrowing what a command threw.
    /// </summary>
    /// <exception cref="TimeoutException">The bot never finished the update.</exception>
    /// <exception cref="InvalidOperationException">Webhook mode: no webhook was set, or it answered with a failure.</exception>
    public Task SendAsync(Update update, CancellationToken token = default) =>
        DeliverAsync(JsonSerializer.SerializeToNode(update, JsonBotAPI.Options)!.AsObject(), token);

    internal async Task DeliverAsync(JsonObject update, CancellationToken token)
    {
        await _bot.DeliverAsync(update, UpdateTimeout, token);
        _errors.ThrowIfAny();
    }

    public ValueTask DisposeAsync() => _bot.DisposeAsync();

    private static void TalkToTheFake(IServiceCollection services, FakeBotApi api, ErrorLog errors)
    {
        services.Replace(ServiceDescriptor.Singleton(new TelegramBotClientAccessor(api.CreateClient())));
        services.Replace(ServiceDescriptor.Scoped<ITelegramErrorHandler>(_ => new RecordingErrorHandler(errors)));
    }

    private interface IRunningBot : IAsyncDisposable
    {
        IServiceProvider Services { get; }

        Task DeliverAsync(JsonObject update, TimeSpan timeout, CancellationToken token);
    }

    // An update is handled once the polling loop asks for the next offset.
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
                var cause =
                    api.RefusedPolling && api.WebhookUrl is { } webhook
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

    private sealed class WebhookBot(
        IAsyncDisposable factory,
        IServiceProvider services,
        HttpClient client,
        FakeBotApi api
    ) : IRunningBot
    {
        private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

        private static readonly TimeSpan AbandonedGrace = TimeSpan.FromSeconds(5);

        // Updates the test stopped waiting for; the bot may still be handling them.
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

            // Giving up only stops waiting: aborting would cancel the bot mid-update and fail the next one.
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

        // Lets cancelled in-flight updates finish before the app's container is disposed.
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
