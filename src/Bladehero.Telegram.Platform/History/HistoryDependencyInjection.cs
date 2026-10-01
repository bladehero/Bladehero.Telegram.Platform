using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.History;

/// <summary>Turns on the bot's history.</summary>
public static class HistoryDependencyInjection
{
    private const string NoStore =
        "Telegram history needs a store: call UseInMemory() or UseEntityFrameworkCore<TContext>() after "
        + "AddTelegramHistory(), or register an ITelegramHistoryStore.";

    /// <summary>
    /// Records every update the bot gets and every Bot API call it makes, stored in the background in the store picked
    /// next.
    /// </summary>
    /// <remarks>Startup fails without a store.</remarks>
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
                .Validate(x => x.QueueCapacity >= 1, "QueueCapacity must be at least 1.")
                .Validate<IServiceProviderIsService>((_, x) => x.IsService(typeof(ITelegramHistoryStore)), NoStore)
                .ValidateOnStart();
            services.AddSingleton<TelegramHistoryWriter>();
            services.AddHostedService(provider => provider.GetRequiredService<TelegramHistoryWriter>());
            services.TryAddSingleton<ITelegramHistory, TelegramHistory>();
        }

        return new TelegramHistoryBuilder(services);
    }
}
