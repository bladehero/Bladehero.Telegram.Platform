using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.History;

/// <summary>Turns on the bot's history.</summary>
public static class HistoryDependencyInjection
{
    private const string NoStore =
        "Telegram history needs a store: call UseInMemory() or UseEntityFrameworkCore<TContext>() after "
        + "AddTelegramHistory(), or register an ITelegramHistoryStore.";

    private const string ClientAfterHistory =
        "The bot's calls wouldn't be recorded: an ITelegramBotClient is registered after AddTelegramHistory(). "
        + "Move AddTelegramHistory() below your own ITelegramBotClient registration "
        + "(e.g. services.AddSingleton<ITelegramBotClient>(...)).";

    /// <summary>
    /// Records every update the bot gets and every Bot API call it makes, stored in the background in the store picked
    /// next.
    /// </summary>
    /// <remarks>
    /// Call it after registering an <see cref="ITelegramBotClient"/> of your own; the library's own registrations may come
    /// before or after. Startup fails without a store, or when a client registered later would go unrecorded. Entries
    /// are stored by a hosted service, so they need a running host.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets how the history is recorded, e.g. <c>h => h.KeepJson = false</c>.</param>
    /// <returns>A builder to pick the store with.</returns>
    public static TelegramHistoryBuilder AddTelegramHistory(
        this IServiceCollection services,
        Action<TelegramHistoryOptions>? configure = null
    )
    {
        var options = services.AddOptions<TelegramHistoryOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        // Once per container, however often it's called: one writer, one set of checks.
        if (!services.Any(x => x.ServiceType == typeof(TelegramHistoryWriter)))
        {
            options
                .Validate(x => x.QueueCapacity >= 1, "Telegram history QueueCapacity must be at least 1.")
                .Validate<IServiceProviderIsService>((_, x) => x.IsService(typeof(ITelegramHistoryStore)), NoStore)
                .ValidateOnStart();

            // Apart from the options above, which building the client reads: checked at start only.
            services
                .AddOptions<ClientOrder>()
                .Validate<IServiceProvider>(
                    (_, provider) => provider.GetService<ITelegramBotClient>() is null or RecordingBotClient,
                    ClientAfterHistory
                )
                .ValidateOnStart();
            services.AddSingleton<TelegramHistoryWriter>();
            services.AddHostedService(provider => provider.GetRequiredService<TelegramHistoryWriter>());
            services.TryAddSingleton<ITelegramHistory, TelegramHistory>();
        }

        // Every call: a client registered since the last one is wrapped too.
        TelegramHistoryClients.Decorate(services);
        return new TelegramHistoryBuilder(services);
    }

    // Carries the startup check that the bot's client records its calls.
    private sealed class ClientOrder;
}
