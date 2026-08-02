using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

/// <summary>
/// Clears an active webhook before the polling loop starts.
/// </summary>
/// <remarks>
/// Telegram serves a bot through <c>getUpdates</c> or a webhook, never both — polling a bot that still
/// has one registered fails with HTTP 409 on every attempt. Asking for long polling settles which of
/// the two the bot is using, so a leftover registration is removed rather than left to break startup.
/// </remarks>
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
                "Deleting the webhook registered at {Url} so long polling can start. Anything still "
                    + "delivering updates to that address will stop receiving them.",
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
