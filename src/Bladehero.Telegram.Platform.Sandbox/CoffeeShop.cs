using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Sandbox;

// The bot's composition root: Program and the component tests register it the same way.
internal static class CoffeeShop
{
    internal static IServiceCollection AddCoffeeShop(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTelegramLongPollingReceiving(configuration, assemblies: typeof(CoffeeShop).Assembly);

        services.Configure<CoffeeShopOptions>(configuration.GetSection(CoffeeShopOptions.Section));
        services.AddSingleton<MemberDirectory>();
        services.AddSingleton<ITelegramUserResolver<Member>, MemberResolver>();
        services.AddSingleton<PointsCard>();

        // External services are off until a real one is registered after these.
        services.AddSingleton<IReceiptReader, DisabledReceiptReader>();
        services.AddSingleton<PendingReceipts>();
        services.AddSingleton<ReceiptCard>();
        services.AddSingleton<ReceiptHistory>();

        return services;
    }
}
