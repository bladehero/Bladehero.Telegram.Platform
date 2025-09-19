using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Telegram.DotNet.Platform.Receiving.Background.Webhook;

internal sealed class TelegramWebhookInitializer(
    ITelegramBotClient client,
    IOptions<TelegramWebhookConfiguration> options
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        await client.SetWebhook(
            configuration.WebhookUri.AbsoluteUri,
            allowedUpdates: configuration.AllowedUpdates,
            dropPendingUpdates: configuration.DropPendingUpdates,
            cancellationToken: cancellationToken
        );
    }

    #region Ignored

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
