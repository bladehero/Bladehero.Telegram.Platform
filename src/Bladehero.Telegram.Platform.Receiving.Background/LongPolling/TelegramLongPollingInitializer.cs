using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

// Telegram serves a bot through getUpdates or a webhook, never both: polling one that still has a
// webhook registered fails with 409 on every attempt.
internal sealed class TelegramLongPollingInitializer(
    TelegramBotClientAccessor accessor,
    IOptions<TelegramReceiverConfiguration> options,
    ILogger<TelegramLongPollingInitializer> logger
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await accessor.Client.GetWebhookInfo(cancellationToken);
            if (string.IsNullOrEmpty(webhook.Url))
            {
                return;
            }

            logger.LogWarning(
                "Deleting the webhook at {Url} so long polling can start - it will stop receiving updates.",
                webhook.Url
            );

            await accessor.Client.DeleteWebhook(options.Value.DropPendingUpdates, cancellationToken);
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
