using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore;

// Deletes entries older than maxAge soon after start, then hourly on the app's clock. It never faults, as a faulted
// background service reads as a stopped bot; a failed pass is logged and the next one tries again.
internal sealed class TelegramHistoryCleanup<TContext>(
    IServiceScopeFactory scopeFactory,
    TimeSpan maxAge,
    ILogger<TelegramHistoryCleanup<TContext>> logger,
    TimeProvider? timeProvider = null
) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    // The app's clock when it registered one; the library registers none.
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Completed after each pass, for tests.
    private TaskCompletionSource _passed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _passes;

    // One pass: the number of entries deleted.
    internal async Task<int> CleanUpAsync(CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cutoff = _time.GetUtcNow() - maxAge;
            var deleted = await scope
                .ServiceProvider.GetRequiredService<TContext>()
                .Set<TelegramHistoryEntry>()
                .Where(x => x.Time < cutoff)
                .ExecuteDeleteAsync(token);
            if (deleted > 0)
            {
                logger.LogInformation(
                    "The Telegram history cleanup deleted {Count} entries older than {MaxAge}.",
                    deleted,
                    maxAge
                );
            }

            return deleted;
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogError(exception, "The Telegram history cleanup failed; it tries again in an hour.");
            return 0;
        }
    }

    // Completes once `count` passes have run.
    internal async Task WaitForPassesAsync(int count, CancellationToken token)
    {
        while (true)
        {
            var next = Volatile.Read(ref _passed).Task;
            if (Volatile.Read(ref _passes) >= count)
            {
                return;
            }

            await next.WaitAsync(token);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the host's start: the first pass may build the context's model.
        await Task.Yield();
        using var timer = new PeriodicTimer(Every, _time);
        try
        {
            do
            {
                await CleanUpAsync(stoppingToken);
                Interlocked.Increment(ref _passes);
                Interlocked
                    .Exchange(ref _passed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                    .TrySetResult();
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
