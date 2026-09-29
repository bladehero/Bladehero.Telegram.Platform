using System.Diagnostics;
using System.Globalization;
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
/// as apologizing to the user, can be checked; if it throws, the action throws an <see cref="AggregateException"/> of
/// the error and then the handler's failure. An error with no update, such as a failed poll, is rethrown by the next
/// action; the errors of an update the test stopped waiting for are dropped.
/// </remarks>
public sealed partial class TelegramTestHost : IAsyncDisposable
{
    private const string MayBeHanging = "A command may be hanging, e.g. on a stub that never completes.";

    // Stopping the bot waits no longer for a command that ignores cancellation.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    // The key the app's own error handler moves to, so the recording one can hand it every error.
    private static readonly object AppsErrorHandler = new();

    // How long an app started with the wrong host method gets to show it, e.g. by polling instead of setting a webhook.
    private static readonly TimeSpan WrongHostGrace = TimeSpan.FromSeconds(1);

    private readonly IRunningBot _bot;
    private readonly ErrorLog _errors;
    private TimeSpan _updateTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30);

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

    /// <summary>
    /// How long each action waits for the bot to finish its update before failing with a
    /// <see cref="TimeoutException"/>: 30 seconds, or no limit when a debugger was attached as the host started.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Zero, or negative other than <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    public TimeSpan UpdateTimeout
    {
        get => _updateTimeout;
        set
        {
            if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The bot needs some time for an update; use Timeout.InfiniteTimeSpan to wait without a limit."
                );
            }

            _updateTimeout = value;
        }
    }

    /// <summary>
    /// Starts a long-polling bot on a generic host: <paramref name="configureServices"/> registers it as its
    /// composition root does, and its real polling loop pulls updates from the fake.
    /// </summary>
    /// <param name="configureServices">The bot's registrations, with <c>AddTelegramLongPollingReceiving</c>.</param>
    /// <param name="api">
    /// A pre-arranged fake, e.g. to fail startup calls, or one an earlier host ran on, which must be disposed first:
    /// one host per fake at a time. A new one when <c>null</c>.
    /// </param>
    /// <param name="token">Stops waiting for the bot to start.</param>
    /// <remarks>
    /// Only the bot client, including one the app registers itself as <see cref="ITelegramBotClient"/> or
    /// <see cref="TelegramBotClient"/> (the token is never used), and the error handler (errors fail the test, after
    /// the app's own handler has seen them) are swapped. Returns once the host has started, so startup work such as the
    /// command menu can be checked right away. The container is validated on build. For an ASP.NET Core app that polls,
    /// use <see cref="ForLongPollingAsync{TEntryPoint}"/>.
    /// </remarks>
    public static Task<TelegramTestHost> ForLongPollingAsync(
        Action<IServiceCollection> configureServices,
        FakeBotApi? api = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(configureServices);

        return ForLongPollingAsync((HostApplicationBuilder builder) => configureServices(builder.Services), api, token);
    }

    /// <summary>
    /// Starts a long-polling bot on a generic host: <paramref name="configure"/> sets it up as its composition root
    /// does, from configuration, environment and logging to services, and its real polling loop pulls updates from
    /// the fake.
    /// </summary>
    /// <param name="configure">
    /// The bot's host builder, in Development with no configuration sources or log providers yet. Register the bot on
    /// its <c>Services</c>, e.g. with <c>AddTelegramLongPollingReceiving(builder.Configuration)</c>.
    /// </param>
    /// <param name="api">
    /// A pre-arranged fake, e.g. to fail startup calls, or one an earlier host ran on, which must be disposed first:
    /// one host per fake at a time. A new one when <c>null</c>.
    /// </param>
    /// <param name="token">Stops waiting for the bot to start.</param>
    /// <remarks>
    /// Swaps the same registrations as the <see cref="IServiceCollection"/> overload, after
    /// <paramref name="configure"/> has run.
    /// </remarks>
    public static async Task<TelegramTestHost> ForLongPollingAsync(
        Action<HostApplicationBuilder> configure,
        FakeBotApi? api = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        api ??= new FakeBotApi();
        var errors = new ErrorLog();

        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        configure(builder);
        TalkToTheFake(builder.Services, api, errors);
        BoundShutdown(builder.Services);
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            )
        );

        var host = builder.Build();
        var running = new StoppingHost(host);
        var pollsBefore = api.Polls;
        try
        {
            await host.StartAsync(token);
        }
        catch
        {
            // Stops the polling loop that may already run.
            await running.DisposeAsync().AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        return new TelegramTestHost(new PollingBot(host.Services, running, api, pollsBefore), api, errors);
    }

    /// <summary>
    /// Starts an ASP.NET Core app that receives by long polling: the app (<typeparamref name="TEntryPoint"/>, usually
    /// <c>Program</c>) runs in memory via <see cref="WebApplicationFactory{TEntryPoint}"/>, and its real polling loop
    /// pulls updates from the fake.
    /// </summary>
    /// <param name="configureWebHost">
    /// Test tweaks: settings the app reads before <c>Build</c> via <c>UseSetting</c>, stand-ins for external services
    /// via <c>ConfigureTestServices</c>.
    /// </param>
    /// <param name="api">
    /// A pre-arranged fake, e.g. to fail startup calls, or one an earlier host ran on, which must be disposed first:
    /// one host per fake at a time. A new one when <c>null</c>.
    /// </param>
    /// <param name="token">Stops waiting for the app to start.</param>
    /// <remarks>
    /// Swaps the same registrations as the generic-host overloads, which suit a bot without a web app, and runs in
    /// Development. Returns once the app has started. An app that sets a webhook instead needs
    /// <see cref="ForWebhookAsync{TEntryPoint}"/>.
    /// </remarks>
    public static async Task<TelegramTestHost> ForLongPollingAsync<TEntryPoint>(
        Action<IWebHostBuilder>? configureWebHost = null,
        FakeBotApi? api = null,
        CancellationToken token = default
    )
        where TEntryPoint : class
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();
        var (factory, app) = CreateApp<TEntryPoint>(configureWebHost, api, errors);
        var pollsBefore = api.Polls;

        // Resolving the app's services starts it.
        var services = await StartAppAsync(() => app.Services, factory, token);

        return new TelegramTestHost(
            new PollingBot(services, factory, api, pollsBefore, typeof(TEntryPoint).Name),
            api,
            errors
        );
    }

    /// <summary>
    /// Starts a webhook bot: its ASP.NET Core app (<typeparamref name="TEntryPoint"/>, usually <c>Program</c>) runs in
    /// memory via <see cref="WebApplicationFactory{TEntryPoint}"/>, and each update is posted to the webhook it set.
    /// </summary>
    /// <param name="configureWebHost">
    /// Test tweaks, e.g. the webhook configuration via <c>UseSetting</c> or <c>ConfigureAppConfiguration</c>.
    /// </param>
    /// <param name="api">
    /// A pre-arranged fake, e.g. to fail startup calls, or one an earlier host ran on, which must be disposed first:
    /// one host per fake at a time. A new one when <c>null</c>.
    /// </param>
    /// <param name="token">Stops waiting for the app to start.</param>
    /// <remarks>
    /// Swaps the same registrations as long polling and runs in Development. Returns once the app has started, so
    /// the webhook it set, if any, is in <see cref="FakeBotApi.WebhookUrl"/>. Updates go to that exact URL (host,
    /// scheme, path), with the secret token header when the bot set one, and follow no redirects and keep no cookies,
    /// as from Telegram.
    /// </remarks>
    public static async Task<TelegramTestHost> ForWebhookAsync<TEntryPoint>(
        Action<IWebHostBuilder>? configureWebHost = null,
        FakeBotApi? api = null,
        CancellationToken token = default
    )
        where TEntryPoint : class
    {
        api ??= new FakeBotApi();
        var errors = new ErrorLog();
        var (factory, app) = CreateApp<TEntryPoint>(configureWebHost, api, errors);
        var pollsBefore = api.Polls;

        // Creating the client starts the app; UpdateTimeout, not HttpClient.Timeout, bounds each update.
        var client = await StartAppAsync(
            () =>
                app.CreateClient(
                    new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }
                ),
            factory,
            token
        );
        client.Timeout = Timeout.InfiniteTimeSpan;

        return new TelegramTestHost(
            new WebhookBot(factory, app.Services, client, api, pollsBefore, typeof(TEntryPoint).Name),
            api,
            errors
        );
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
    /// <exception cref="TimeoutException">
    /// The bot did not finish the update within <see cref="UpdateTimeout"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Telegram would not send this type of update to the bot (<c>allowed_updates</c>). Long polling: the bot's host
    /// stopped. Webhook mode: no webhook is set, or it answered with a failure.
    /// </exception>
    public Task SendAsync(Update update, CancellationToken token = default)
    {
        var json = JsonSerializer.SerializeToNode(update, JsonBotAPI.Options)!.AsObject();
        return DeliverAsync(FakeBotApi.UpdateTypeOf(json), () => json, token);
    }

    // `compose` builds the update of `updateType`, e.g. posting the user's message into the chat, once the bot can
    // take it and Telegram would send it.
    internal async Task DeliverAsync(string? updateType, Func<JsonObject> compose, CancellationToken token)
    {
        long? updateId = null;
        try
        {
            await _bot.DeliverAsync(
                () =>
                {
                    Api.ThrowIfNotAllowed(updateType);
                    var update = compose();
                    Api.KnowChatsIn(update);
                    return update;
                },
                UpdateTimeout,
                id => updateId = id,
                token
            );
        }
        catch when (updateId is not null)
        {
            _errors.Abandon(updateId.Value);
            throw;
        }

        _errors.ThrowFor(updateId);
    }

    /// <summary>Stops the bot and disposes its host; the fake keeps its chats for a host started on it again.</summary>
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

        var appsOwn = errorHandlers.LastOrDefault();
        if (appsOwn is not null)
        {
            services.Add(KeyedAs(AppsErrorHandler, appsOwn));
        }

        // With the app handler's lifetime, so a singleton that injects the error handler still gets one.
        services.Add(
            new ServiceDescriptor(
                typeof(ITelegramErrorHandler),
                provider => new RecordingErrorHandler(errors, provider),
                appsOwn?.Lifetime ?? ServiceLifetime.Scoped
            )
        );

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

    // Disposing the factory disposes the derived one that runs the app.
    private static (
        WebApplicationFactory<TEntryPoint> Factory,
        WebApplicationFactory<TEntryPoint> App
    ) CreateApp<TEntryPoint>(Action<IWebHostBuilder>? configureWebHost, FakeBotApi api, ErrorLog errors)
        where TEntryPoint : class
    {
        var factory = new WebApplicationFactory<TEntryPoint>();
        var app = factory.WithWebHostBuilder(web =>
        {
            configureWebHost?.Invoke(web);
            web.ConfigureTestServices(services =>
            {
                TalkToTheFake(services, api, errors);
                BoundShutdown(services);
            });
        });

        return (factory, app);
    }

    // Starting a factory's app blocks, so it runs on the thread pool, and the token only stops waiting for it. An app
    // that fails to start, or that the test gave up on, is disposed.
    private static async Task<T> StartAppAsync<T>(Func<T> start, IAsyncDisposable factory, CancellationToken token)
    {
        var starting = Task.Run(start, CancellationToken.None);
        try
        {
            return await starting.WaitAsync(token);
        }
        catch (OperationCanceledException) when (!starting.IsCompleted)
        {
            _ = DisposeOnceStartedAsync();
            throw;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }

        async Task DisposeOnceStartedAsync()
        {
            try
            {
                await ((Task)starting).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await factory.DisposeAsync();
            }
            catch
            {
                // Nothing waits for this any more, so there is no one to tell.
            }
        }
    }

    private static void BoundShutdown(IServiceCollection services) =>
        services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);

    // What is left of `timeout` after `clock`'s time; infinite stays so.
    internal static TimeSpan Left(TimeSpan timeout, Stopwatch clock)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return timeout;
        }

        var left = timeout - clock.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    // The limit, or the cap when that comes sooner; no limit comes later than any cap.
    private static TimeSpan AtMost(TimeSpan limit, TimeSpan cap) =>
        limit == Timeout.InfiniteTimeSpan || limit > cap ? cap : limit;

    // "500 ms", "1 second", "1.5 seconds".
    internal static string Describe(TimeSpan duration) =>
        duration < TimeSpan.FromSeconds(1)
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0.###} ms")
        : duration == TimeSpan.FromSeconds(1) ? "1 second"
        : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.###} seconds");

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

        // Composes the update once the bot can take it and returns once the bot has finished it, passing its id to
        // `numbered` as soon as it has one.
        Task DeliverAsync(Func<JsonObject> compose, TimeSpan timeout, Action<long> numbered, CancellationToken token);
    }
}
