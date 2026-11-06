using Bladehero.Telegram.Platform.Hub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Receiving.Background;

internal static class TelegramHubRegistrationDependencyInjection
{
    // A start-up step a hub may have to hear again: one instance, as it remembers how the start went.
    internal static void AddRepeatedForTheHub<T>(this IServiceCollection services)
        where T : class, IHostedService, ITelegramHubRegistration
    {
        services.TryAddSingleton<T>();
        services.AddHostedService(provider => provider.GetRequiredService<T>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITelegramHubRegistration, T>(provider => provider.GetRequiredService<T>())
        );
    }
}
