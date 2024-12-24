using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace Telegram.DotNet.Platform.Receiving.Background;

internal class TelegramReceivingService(IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var client = provider.GetRequiredService<ITelegramBotClient>();
        var optionsProvider = provider.GetRequiredService<IReceiverOptionsProvider>();
        var handler = provider.GetRequiredService<IUpdateHandler>();
        var options = optionsProvider.Get();
        await client.ReceiveAsync(handler, options, stoppingToken);
    }
}
