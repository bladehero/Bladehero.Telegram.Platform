using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

// Polling a bot that still has a webhook fails with 409, so the webhook is deleted first.
internal sealed class TelegramLongPollingInitializer(
    ITelegramBotClient client,
    IOptions<TelegramReceiverConfiguration> options,
    ILogger<TelegramLongPollingInitializer> logger
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await client.GetWebhookInfo(cancellationToken);
            if (string.IsNullOrEmpty(webhook.Url))
            {
                return;
            }

            logger.LogWarning(
                "Deleting the webhook at {Url} so long polling can start - it will stop receiving updates.",
                webhook.Url
            );

            await client.DeleteWebhook(options.Value.DropPendingUpdates, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to clear the webhook before starting long polling");
        }
    }

    #region Ignored

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
