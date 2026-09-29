using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

internal sealed class TelegramLongPollingBackgroundService(
    ITelegramBotClient client,
    ScopedUpdateHandler updateHandler,
    IOptions<TelegramReceiverConfiguration> options
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        client.ReceiveAsync(updateHandler, options.Value.ToOptions(), stoppingToken);
}
