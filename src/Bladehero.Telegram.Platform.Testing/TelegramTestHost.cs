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
        BoundShutdown(builder.Services);
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            )
        );

        var bot = new PollingBot(builder.Build(), api);
        try
        {
            await bot.StartAsync(token);
        }
        catch
        {
            // Stops the polling loop that may already run.
            await bot.DisposeAsync().AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        return new TelegramTestHost(bot, api, errors);
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
            web.ConfigureTestServices(services =>
            {
                TalkToTheFake(services, api, errors);
                BoundShutdown(services);
            });
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
    /// <exception cref="TimeoutException">
    /// The bot did not finish the update within <see cref="UpdateTimeout"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Long polling: the bot's host stopped. Webhook mode: no webhook is set, or it answered with a failure.
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

    private static void BoundShutdown(IServiceCollection services) =>
        services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);

    // "500 ms", "1 second", "1.5 seconds".
    private static string Describe(TimeSpan duration) =>
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

        // Returns once the bot has finished the update, passing its id to `numbered` as soon as it has one.
        Task DeliverAsync(JsonObject update, TimeSpan timeout, Action<long> numbered, CancellationToken token);
    }
}
