using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Telegram.DotNet.Platform.Receiving.Background.Webhook;

internal sealed class TelegramWebhookInitializer(
    ITelegramBotClient client,
    IOptions<TelegramWebhookConfiguration> options,
    ILogger<TelegramWebhookInitializer> logger
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        try
        {
            var webhook = await client.GetWebhookInfo(cancellationToken);
            if (webhook.HasNoChangesBasedOn(configuration))
            {
                return;
            }

            await client.DeleteWebhook(cancellationToken: cancellationToken);

            await client.SetWebhook(
                configuration.WebhookUri.AbsoluteUri,
                allowedUpdates: configuration.AllowedUpdates,
                dropPendingUpdates: configuration.DropPendingUpdates,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to initialize webhook");
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
