using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Bladehero.Telegram.Platform.Sandbox.Barista;
using Bladehero.Telegram.Platform.Sandbox.Coffee;
using Bladehero.Telegram.Platform.Sandbox.Failures;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Bladehero.Telegram.Platform.Sandbox.Voice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Sandbox;

// The bot's composition root: Program and the component tests register it the same way.
internal static class CoffeeShop
{
    internal static IServiceCollection AddCoffeeShop(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTelegramLongPollingReceiving(configuration, assemblies: typeof(CoffeeShop).Assembly);
        services.AddScoped<ITelegramErrorHandler, ApologizingErrorHandler>(); // after receiving, to replace its own
        services.Configure<CoffeeShopOptions>(configuration.GetSection(CoffeeShopOptions.Section));
        services.TryAddSingleton(TimeProvider.System);

        // The loyalty club.
        services.AddSingleton<MemberDirectory>();
        services.AddSingleton<ITelegramUserResolver<Member>, MemberResolver>();
        services.AddSingleton<PointsCard>();

        // Coffee orders, and the barista who announces them when brewed.
        services.AddScoped<CoffeeOrdering>();
        services.AddSingleton<OrderQueue>();
        services.AddHostedService<BaristaService>();

        // Receipts.
        services.AddSingleton<PendingReceipts>();
        services.AddSingleton<ReceiptCard>();
        services.AddSingleton<ReceiptHistory>();
        services.AddSingleton<ReceiptAlbums>();

        // External services, off until real ones are registered after these.
        services.AddSingleton<IReceiptReader, DisabledReceiptReader>();
        services.AddSingleton<ITranscriber, DisabledTranscriber>();

        if (configuration.GetSection(CoffeeShopOptions.Section).GetValue<bool>("Demo"))
        {
            services.AddSingleton<IReceiptReader, DemoReceiptReader>();
            services.AddSingleton<ITranscriber, DemoTranscriber>();
        }

        return services;
    }
}
