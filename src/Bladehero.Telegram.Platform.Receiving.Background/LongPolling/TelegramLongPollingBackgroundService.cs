using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

internal sealed class TelegramLongPollingBackgroundService(
    TelegramBotClientAccessor accessor,
    ScopedUpdateHandler updateHandler,
    IOptions<TelegramReceiverConfiguration> options
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        accessor.Client.ReceiveAsync(updateHandler, options.Value.ToOptions(), stoppingToken);
}
