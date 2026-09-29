using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Sandbox;

// The bot's composition root: Program and the component tests register it the same way.
internal static class CoffeeShop
{
    internal static IServiceCollection AddCoffeeShop(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTelegramLongPollingReceiving(configuration, assemblies: typeof(CoffeeShop).Assembly);
        return services;
    }
}
