using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Hub;

// Waits a while at the app's start for the first list of people, then keeps the copy fresh. The app never stops for the
// hub: whatever fails here is logged and tried again, and the last copy keeps working.
internal sealed class TelegramHubRefresh(
    TelegramHubClient hub,
    IEnumerable<ITelegramHubRegistration> registrations,
    IOptions<TelegramHubConfiguration> configuration,
    ILogger<TelegramHubRefresh> logger,
    TimeProvider time
) : IHostedLifecycleService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _running;
    private bool _manifestSent;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waiting.CancelAfter(configuration.Value.StartWait);

        try
        {
            while (!await RefreshAsync(waiting.Token))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time, waiting.Token);
            }
        }
        // Not only a cancellation: Telegram.Bot reports a call cut short while its answer is read as a RequestException.
        catch (Exception) when (waiting.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "The hub didn't answer within {Wait}: signed-in people stay, and new sign-ins wait for it",
                configuration.Value.StartWait
            );
        }
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _running = KeepFreshAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_running is not null)
        {
            await _running;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task KeepFreshAsync(CancellationToken stopping)
    {
        var settings = configuration.Value;
        var succeeded = hub.KnowsPeople && _manifestSent;

        try
        {
            while (true)
            {
                var wait =
                    succeeded || settings.RetryEvery > settings.RefreshEvery
                        ? settings.RefreshEvery
                        : settings.RetryEvery;
                await Task.Delay(wait, time, stopping);
                succeeded = await RefreshAsync(stopping) && await RepeatRegistrationsAsync(stopping);
            }
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            // The app is stopping, whatever the call it cut short threw.
        }
    }

    private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Every time, so a hub that lost its file learns about this app again without a restart. The loop's
            // RepeatRegistrationsAsync does the same for the webhook and the commands.
            await hub.SendManifestAsync(cancellationToken);
            _manifestSent = true;
            if (!await hub.ReadPeopleAsync(cancellationToken))
            {
                logger.LogWarning("The hub lists nobody for this app: check its settings. The last list stays");
            }

            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Failed to reach the hub");
            return false;
        }
    }

    // Not at the start, where the receivers are about to do it themselves.
    private async Task<bool> RepeatRegistrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var registration in registrations)
            {
                await registration.RepeatAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Failed to tell the hub about the webhook or the commands");
            return false;
        }
    }
}
