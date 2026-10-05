using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bladehero.Telegram.Platform.Receiving;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Testing;

// The bot's ITelegramLock, held or watched by the test to stage a race without sleeps.
public sealed partial class TelegramTestHost
{
    // Resolved once; holds null when the app doesn't use the lock.
    private StrongBox<TelegramLock?>? _telegramLock;

    /// <summary>
    /// Takes the bot's <see cref="ITelegramLock"/> until disposed, so every update and every <c>RunAsync</c> waits:
    /// the way to stage a race. An action such as <c>SendsAsync</c> then can't finish, so start it without awaiting
    /// it, and await it once the lock is released.
    /// </summary>
    /// <param name="token">Stops waiting for the lock.</param>
    /// <returns>The hold; disposing it releases the lock.</returns>
    /// <exception cref="InvalidOperationException">The app doesn't call <c>AddTelegramLock()</c>.</exception>
    /// <exception cref="TimeoutException">The lock wasn't free within <see cref="UpdateTimeout"/>.</exception>
    public async Task<IAsyncDisposable> HoldLockAsync(CancellationToken token = default)
    {
        var telegramLock = RequiredLock;
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        waiting.CancelAfter(UpdateTimeout);
        try
        {
            return await telegramLock.HoldAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Telegram lock wasn't free within {Describe(UpdateTimeout)}: {StateOf(telegramLock)}. Is an "
                    + "update or a RunAsync's work hanging, or was an earlier hold not disposed?"
            );
        }
    }

    /// <summary>
    /// Returns once at least <paramref name="count"/> updates and <c>RunAsync</c> calls wait for the bot's
    /// <see cref="ITelegramLock"/>, e.g. behind <see cref="HoldLockAsync"/>.
    /// </summary>
    /// <param name="count">How many waiters to wait for, at least 1.</param>
    /// <param name="timeout">How long to wait; <see cref="UpdateTimeout"/> by default.</param>
    /// <param name="token">Stops waiting.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is below 1, or <paramref name="timeout"/> isn't positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">The app doesn't call <c>AddTelegramLock()</c>.</exception>
    /// <exception cref="TimeoutException">Fewer waited in time; the error says how many, and whether it's held.</exception>
    public async Task WaitForLockWaitersAsync(int count, TimeSpan? timeout = null, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var limit = timeout ?? UpdateTimeout;
        if (limit <= TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), limit, "Waiting needs some time.");
        }

        var telegramLock = RequiredLock;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            // Taken before the check, so a change right after it still ends the wait below.
            var changed = telegramLock.Changed;
            if (telegramLock.WaitingCount >= count)
            {
                return;
            }

            try
            {
                await changed.WaitAsync(Left(limit, clock), token);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"Fewer than {count} waited for the Telegram lock within {Describe(limit)}: "
                        + $"{StateOf(telegramLock)}."
                );
            }
        }
    }

    private TelegramLock RequiredLock =>
        (_telegramLock ??= new(Services.GetService<TelegramLock>())).Value
        ?? throw new InvalidOperationException(
            "The bot doesn't use the Telegram lock; register it with AddTelegramLock()."
        );

    private static string StateOf(TelegramLock telegramLock) =>
        $"WaitingCount is {telegramLock.WaitingCount} and IsHeld is {(telegramLock.IsHeld ? "true" : "false")}";
}
