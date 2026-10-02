using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Barista;

// Tells each customer when their coffee is ready: a message the bot sends on its own, long after the update that
// placed the order.
internal sealed class BaristaService(OrderQueue queue, ITelegramMessages messages, ILogger<BaristaService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (order, brewed) in queue.ReadAllAsync(stoppingToken))
        {
            await brewed.WaitAsync(stoppingToken);

            try
            {
                await messages.SendAsync(
                    order.ChatId,
                    $"☕ Your {order.Size} coffee for {order.CupName} is ready!",
                    token: stoppingToken
                );
            }
            catch (RequestException error)
            {
                // Telegram refused it, e.g. the customer blocked the bot, or it never got there; the next customer
                // still gets their coffee.
                logger.LogWarning(error, "Could not tell chat {ChatId} that their coffee is ready.", order.ChatId);
            }
        }
    }
}
