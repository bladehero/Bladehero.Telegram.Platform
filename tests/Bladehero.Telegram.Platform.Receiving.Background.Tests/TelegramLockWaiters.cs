namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

internal static class TelegramLockWaiters
{
    // Only bounds a failing test's wait; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Returns once at least `count` wait for the lock, looking again on each of its changes rather than polling.
    public static async Task WaitForWaitersAsync(this ITelegramLock telegramLock, int count)
    {
        var watched = (TelegramLock)telegramLock;
        using var patience = new CancellationTokenSource(Patience);
        while (true)
        {
            var changed = watched.Changed;
            if (watched.WaitingCount >= count)
            {
                return;
            }

            await changed.WaitAsync(patience.Token);
        }
    }
}
