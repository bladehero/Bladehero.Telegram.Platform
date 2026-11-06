using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Registers an app behind a hub: a service that owns the bot and answers each app as if it were the Bot API.</summary>
public static class TelegramHubDependencyInjection
{
    /// <summary>
    /// Sends the hub this app's manifest on start, and keeps a copy of its people as <see cref="ITelegramHub"/>.
    /// </summary>
    /// <remarks>
    /// The bot itself is registered as usual, with the app's key as its token and the hub as its <c>ApiBaseUrl</c>. Start
    /// waits up to <see cref="TelegramHubConfiguration.StartWait"/> for the first list. A hub that's down never stops the
    /// app: see <see cref="ITelegramHub.KnowsPeople"/>. Each refresh also tells it about the receiver's webhook and commands
    /// again, if it missed them or has forgotten them.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets what the app says about itself, at least its title.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTelegramHub(
        this IServiceCollection services,
        Action<TelegramHubConfiguration> configure
    )
    {
        services
            .AddOptions<TelegramHubConfiguration>()
            .Configure(configure)
            .Validate(
                configuration => !string.IsNullOrWhiteSpace(configuration.Title),
                "The hub needs the app's Title."
            )
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TelegramHubClient>();
        services.TryAddSingleton<ITelegramHub>(provider => provider.GetRequiredService<TelegramHubClient>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TelegramHubRefresh>());

        return services;
    }
}
