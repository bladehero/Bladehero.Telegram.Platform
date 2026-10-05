using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Receiving;

/// <summary>Turns on the <see cref="ITelegramLock"/>.</summary>
public static class TelegramLockDependencyInjection
{
    /// <summary>
    /// Registers <see cref="ITelegramLock"/>, which every update then holds while it is handled, under long polling and
    /// the webhook alike.
    /// </summary>
    /// <remarks>
    /// Opt-in, as it makes a webhook bot handle one update at a time; a polling bot already does. Call it before or
    /// after the receiving setup; calling it again changes nothing.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTelegramLock(this IServiceCollection services)
    {
        services.TryAddSingleton<TelegramLock>();
        services.TryAddSingleton<ITelegramLock>(provider => provider.GetRequiredService<TelegramLock>());
        return services;
    }
}
