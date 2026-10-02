using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore;

/// <summary>Keeps the bot's history in a database through Entity Framework Core.</summary>
public static class EntityFrameworkCoreHistoryDependencyInjection
{
    /// <summary>
    /// Keeps the history in <typeparamref name="TContext"/>'s database, written through a context instance of its own,
    /// apart from the app's changes; with <paramref name="maxAge"/>, older entries are deleted hourly.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TContext"/> maps the history with <c>MapTelegramHistory</c>; startup fails when it doesn't,
    /// or when it isn't registered.
    /// </remarks>
    /// <typeparam name="TContext">The app's own context.</typeparam>
    /// <param name="history">The history, from <c>AddTelegramHistory</c>.</param>
    /// <param name="maxAge">How long entries are kept; <c>null</c> keeps them all.</param>
    /// <returns><paramref name="history"/>, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> isn't positive.</exception>
    public static TelegramHistoryBuilder UseEntityFrameworkCore<TContext>(
        this TelegramHistoryBuilder history,
        TimeSpan? maxAge = null
    )
        where TContext : DbContext
    {
        if (maxAge is { } age)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero, nameof(maxAge));
        }

        // By factory, so a missing context fails with the check below rather than the container's validation.
        var services = history.Services;
        services.AddScoped<ITelegramHistoryStore>(provider => new EntityFrameworkCoreTelegramHistoryStore<TContext>(
            provider.GetRequiredService<TContext>()
        ));

        // Checked at start only, in a scope of its own, apart from the options the writer reads when it's built.
        if (!services.Any(x => x.ServiceType == typeof(IValidateOptions<ContextCheck<TContext>>)))
        {
            var name = typeof(TContext).Name;
            services
                .AddOptions<ContextCheck<TContext>>()
                .Validate<IServiceProviderIsService>(
                    (_, registered) => registered.IsService(typeof(TContext)),
                    $"{name} isn't registered; add it with AddDbContext<{name}>()."
                )
                .Validate<IServiceScopeFactory>(
                    (_, scopes) => MapsTheHistory<TContext>(scopes),
                    $"{name} doesn't map the Telegram history: call modelBuilder.MapTelegramHistory() in its "
                        + "OnModelCreating, then add a migration."
                )
                .ValidateOnStart();
        }

        if (maxAge is { } keep)
        {
            services.AddHostedService(provider => new TelegramHistoryCleanup<TContext>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                keep,
                provider.GetRequiredService<ILogger<TelegramHistoryCleanup<TContext>>>(),
                provider.GetService<TimeProvider>()
            ));
        }

        return history;
    }

    // An unregistered context is the other check's to report.
    private static bool MapsTheHistory<TContext>(IServiceScopeFactory scopes)
        where TContext : DbContext
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetService<TContext>() is not { } context
            || context.Model.FindEntityType(typeof(TelegramHistoryEntry)) is not null;
    }

    // Carries the startup checks of TContext.
    private sealed class ContextCheck<TContext>;
}
