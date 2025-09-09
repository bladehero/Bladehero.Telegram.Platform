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
        using var scope = serviceScopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var client = provider.GetRequiredService<ITelegramBotClient>();
        var handler = provider.GetRequiredService<IUpdateHandler>();
        var options = provider.GetRequiredService<IOptions<ReceiverConfiguration>>().Value.ToOptions();
        await client.ReceiveAsync(handler, options, stoppingToken);
    }
}
