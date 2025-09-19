using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace Telegram.DotNet.Platform.Receiving.Background.LongPolling;

internal class TelegramLongPollingBackgroundService(IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var client = provider.GetRequiredService<ITelegramBotClient>();
        var handler = provider.GetRequiredService<IUpdateHandler>();
        var options = provider.GetRequiredService<IOptions<TelegramReceiverConfiguration>>().Value.ToOptions();
        await client.ReceiveAsync(handler, options, stoppingToken);
    }
}
