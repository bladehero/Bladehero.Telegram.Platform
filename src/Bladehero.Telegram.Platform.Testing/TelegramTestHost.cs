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
/// <remarks>
/// An error belongs to the action that caused it: each action rethrows the first error raised by its own update, even
/// when several users act at once. The app's own <see cref="ITelegramErrorHandler"/> still runs, so what it does, such
/// as apologizing to the user, can be checked. An error with no update, such as a failed poll, is rethrown by the next
/// action; the errors of an update the test stopped waiting for are dropped.
/// </remarks>
public sealed class TelegramTestHost : IAsyncDisposable
{
    // The key the app's own error handler moves to, so the recording one can hand it every error.
    private static readonly object AppsErrorHandler = new();

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
    /// Starts a long-polling bot: <paramref name="configureServices"/> registers it as its composition root does, and
    /// its real polling loop pulls updates from the fake.
    /// </summary>
    /// <param name="configureServices">The bot's registrations, with <c>AddTelegramLongPollingReceiving</c>.</param>
    /// <param name="api">A pre-arranged fake, e.g. to fail startup calls; a new one when <c>null</c>.</param>
    /// <param name="token">Stops waiting for the bot to start.</param>
    /// <remarks>
    /// Only the bot client, including one the app registers itself as <see cref="ITelegramBotClient"/> or
    /// <see cref="TelegramBotClient"/> (the token is never used), and the error handler (errors fail the test, after
    /// the app's own handler has seen them) are swapped. Returns once the host has started, so startup work such as the
    /// command menu can be checked right away. The container is validated on build.
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
    /// <param name="configureWebHost">
    /// Test tweaks, e.g. the webhook configuration via <c>ConfigureAppConfiguration</c>.
    /// </param>
    /// <param name="api">A pre-arranged fake, e.g. to fail startup calls; a new one when <c>null</c>.</param>
    /// <remarks>
    /// Swaps the same two registrations as long polling and runs in Development. Returns once the app has started, so
    /// the webhook it set, if any, is in <see cref="FakeBotApi.WebhookUrl"/>. Updates go to that exact URL (host,
    /// scheme, path), with the secret token header when the bot set one, and follow no redirects and keep no cookies,
    /// as from Telegram.
    /// </remarks>
    public static async Task<TelegramTestHost> ForWebhookAsync<TEntryPoint>(
        Action<IWebHostBuilder>? configureWebHost = null,
        FakeBotApi? api = null
    )
        where TEntryPoint : class
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();

        // Disposing the factory disposes the derived one that runs the app.
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

    /// <summary>
    /// The group <paramref name="title"/> with the bot in it (the same title is the same group); add people with
    /// <see cref="TestChat.Member"/>.
    /// </summary>
    public TestChat GroupChat(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new TestChat(this, Api.Group(title), title, isGroup: true);
    }

    /// <summary>
    /// Delivers a raw <paramref name="update"/> (it is not added to any <see cref="TestChat"/>) and returns once it is
    /// handled, rethrowing the first error it raised.
    /// </summary>
    /// <exception cref="TimeoutException">The bot never finished the update.</exception>
    /// <exception cref="InvalidOperationException">
    /// Webhook mode: no webhook is set, or it answered with a failure.
    /// </exception>
    public Task SendAsync(Update update, CancellationToken token = default) =>
        DeliverAsync(JsonSerializer.SerializeToNode(update, JsonBotAPI.Options)!.AsObject(), token);

    internal async Task DeliverAsync(JsonObject update, CancellationToken token)
    {
        long? updateId = null;
        try
        {
            await _bot.DeliverAsync(update, UpdateTimeout, id => updateId = id, token);
        }
        catch when (updateId is not null)
        {
            _errors.Abandon(updateId.Value);
            throw;
        }

        _errors.ThrowFor(updateId);
    }

    public ValueTask DisposeAsync() => _bot.DisposeAsync();

    private static void TalkToTheFake(IServiceCollection services, FakeBotApi api, ErrorLog errors)
    {
        var client = (TelegramBotClient)api.CreateClient();
        services.Replace(ServiceDescriptor.Singleton(new TelegramBotClientAccessor(client)));

        // The app's effective error handler moves to a key, and the recording one hands it every error it records.
        var errorHandlers = services
            .Where(x => x.ServiceType == typeof(ITelegramErrorHandler) && !x.IsKeyedService)
            .ToArray();

        foreach (var descriptor in errorHandlers)
        {
            services.Remove(descriptor);
        }

        if (errorHandlers is [.., var appsOwn])
        {
            services.Add(KeyedAs(AppsErrorHandler, appsOwn));
        }

        services.AddScoped<ITelegramErrorHandler>(provider => new RecordingErrorHandler(errors, provider));

        // A client the app registers itself, e.g. for messages it starts, talks to the fake as well.
        var ownClients = services
            .Where(x => x.ServiceType == typeof(ITelegramBotClient) || x.ServiceType == typeof(TelegramBotClient))
            .ToArray();

        foreach (var descriptor in ownClients)
        {
            services.Remove(descriptor);
        }

        foreach (var (type, key) in ownClients.Select(x => (x.ServiceType, x.ServiceKey)).Distinct())
        {
            services.Add(
                key is null
                    ? ServiceDescriptor.Singleton(type, client)
                    : ServiceDescriptor.KeyedSingleton(type, key, client)
            );
        }
    }

    // Keeps the registration's lifetime, so the handler is built and disposed as in the app.
    private static ServiceDescriptor KeyedAs(object key, ServiceDescriptor descriptor) =>
        descriptor switch
        {
            { ImplementationInstance: { } instance } => new ServiceDescriptor(descriptor.ServiceType, key, instance),
            { ImplementationFactory: { } factory } => new ServiceDescriptor(
                descriptor.ServiceType,
                key,
                (provider, _) => factory(provider),
                descriptor.Lifetime
            ),
            _ => new ServiceDescriptor(
                descriptor.ServiceType,
                key,
                descriptor.ImplementationType!,
                descriptor.Lifetime
            ),
        };

    private interface IRunningBot : IAsyncDisposable
    {
        IServiceProvider Services { get; }

        // Returns once the bot has finished the update, passing its id to `numbered` as soon as it has one.
        Task DeliverAsync(JsonObject update, TimeSpan timeout, Action<long> numbered, CancellationToken token);
    }

    // An update is handled once the polling loop asks for the next offset.
    private sealed class PollingBot(IHost host, FakeBotApi api) : IRunningBot
    {
        public IServiceProvider Services => host.Services;

        public async Task DeliverAsync(
            JsonObject update,
            TimeSpan timeout,
            Action<long> numbered,
            CancellationToken token
        )
        {
            var updateId = api.Enqueue(update);
            numbered(updateId);

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

        public async Task DeliverAsync(
            JsonObject update,
            TimeSpan timeout,
            Action<long> numbered,
            CancellationToken token
        )
        {
            var webhook =
                api.Webhook()
                ?? throw new InvalidOperationException(
                    "The bot has set no webhook, so there is nowhere to post the update. Is webhook receiving "
                        + "registered, for example with AddTelegramWebhookReceiving and UseTelegramWebhook, and did "
                        + "its setWebhook succeed? Api.Calls shows what the bot asked Telegram."
                );

            var (stamped, updateId) = api.StampForWebhook(update);
            numbered(updateId);

            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(stamped.ToJsonString(), Encoding.UTF8, "application/json"),
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
                            // Observed, so a later failure is not reported as an unobserved task exception.
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

    // Errors by the update that raised them; a null update is a failed poll.
    private sealed class ErrorLog
    {
        private readonly List<Recorded> _errors = [];

        // Updates whose action is over: it returned, or the test stopped waiting for it.
        private readonly HashSet<long> _over = [];

        public void Add(long? updateId, Exception exception)
        {
            lock (_errors)
            {
                if (updateId is not { } id || !_over.Contains(id))
                {
                    _errors.Add(new Recorded(updateId, exception));
                }
            }
        }

        // Rethrows the first error of the update, else the first with no update; the update's errors are forgotten.
        public void ThrowFor(long? updateId)
        {
            Exception? first;
            lock (_errors)
            {
                first = updateId is { } id ? Forget(id) : null;
                first ??= Forget(x => x.UpdateId is null);
            }

            if (first is not null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }

        public void Abandon(long updateId)
        {
            lock (_errors)
            {
                Forget(updateId);
            }
        }

        private Exception? Forget(long updateId)
        {
            _over.Add(updateId);
            return Forget(x => x.UpdateId == updateId);
        }

        private Exception? Forget(Predicate<Recorded> match)
        {
            var first = _errors.Find(match)?.Exception;
            _errors.RemoveAll(match);
            return first;
        }

        private sealed record Recorded(long? UpdateId, Exception Exception);
    }

    // Records each error for the action that caused it, then hands it to the app's own handler in the same scope.
    private sealed class RecordingErrorHandler(ErrorLog errors, IServiceProvider services) : ITelegramErrorHandler
    {
        public async Task HandleAsync(TelegramError telegramError)
        {
            var updateId = telegramError.Update?.Id;
            errors.Add(updateId, telegramError.Exception);

            try
            {
                if (services.GetKeyedService<ITelegramErrorHandler>(AppsErrorHandler) is { } appsOwn)
                {
                    await appsOwn.HandleAsync(telegramError);
                }
            }
            catch (Exception exception)
            {
                errors.Add(updateId, exception);
            }
        }
    }
}
