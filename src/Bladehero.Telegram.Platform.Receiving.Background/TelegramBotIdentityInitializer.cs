using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Receiving.Background;

// Learns the bot's username before the other initializers run, so IsCommand can refuse /start@other_bot; a failure
// only warns, as the username is asked for again on later updates.
internal sealed class TelegramBotIdentityInitializer(
    ITelegramBotIdentity identity,
    ILogger<TelegramBotIdentityInitializer> logger
) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await identity.GetAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Couldn't learn the bot's username with getMe; commands addressed to a bot (/start@name) are "
                    + "accepted for any name until it can."
            );
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
