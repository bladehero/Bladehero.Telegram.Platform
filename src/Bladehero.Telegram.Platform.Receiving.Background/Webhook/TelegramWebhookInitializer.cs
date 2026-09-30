using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

internal sealed class TelegramWebhookInitializer(
    ITelegramBotClient client,
    IOptions<TelegramWebhookConfiguration> options,
    ILogger<TelegramWebhookInitializer> logger,
    ITelegramBotIdentity identity
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        try
        {
            logger.LogDebug("Starting webhook initializer");

            // The identity initializer's getMe, unless it failed.
            var me = await identity.GetAsync(cancellationToken);
            logger.LogDebug("Bot info: {Id}, @{Username}", me.Id, me.Username);

            var webhook = await client.GetWebhookInfo(cancellationToken);
            logger.LogDebug("Current webhook info: {@Info}", webhook);

            // getWebhookInfo never shows the secret token, so with one configured the webhook is set on every start;
            // Telegram takes an identical setWebhook as a no-op, so nothing is deleted then.
            var changed = webhook.HasChangesBasedOn(configuration);
            if (!changed && !configuration.HasSecretToken)
            {
                logger.LogInformation("No webhook changes detected, keeping as it was...");
                return;
            }

            if (changed)
            {
                logger.LogInformation("Webhook will be re-applied now");
                await client.DeleteWebhook(cancellationToken: cancellationToken);
                logger.LogInformation("Webhook has been deleted");
            }

            // Unset asks for Telegram's default explicitly: an omitted list would keep one set before.
            await client.SetWebhook(
                configuration.WebhookUri.AbsoluteUri,
                allowedUpdates: configuration.AllowedUpdates ?? [],
                dropPendingUpdates: configuration.DropPendingUpdates,
                secretToken: configuration.HasSecretToken ? configuration.SecretToken : null,
                cancellationToken: cancellationToken
            );

            logger.LogInformation("Webhook has been applied with the latest changes");
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
