namespace Bladehero.Telegram.Platform.Receiving;

/// <summary>
/// The lock every update holds while it is handled, so the app's background work can run one at a time with the
/// updates; turned on by <see cref="TelegramLockDependencyInjection.AddTelegramLock"/>.
/// </summary>
public interface ITelegramLock
{
    /// <summary>Whether an update or some work holds the lock now.</summary>
    bool IsHeld { get; }

    /// <summary>How many updates and <c>RunAsync</c> calls wait for the lock now.</summary>
    int WaitingCount { get; }

    /// <summary>
    /// Waits for the lock, runs <paramref name="work"/> in a DI scope of its own, and releases the lock, even when the
    /// work throws.
    /// </summary>
    /// <param name="work">
    /// The work, given the scope's services and <paramref name="token"/>. Keep it short: every update waits behind it.
    /// </param>
    /// <param name="token">Cancels the wait; the work gets it too.</param>
    /// <remarks>
    /// Waiters are served in arrival order. The Bot API calls the work makes aren't linked to any update in the history.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The calling code already runs under the lock, e.g. a command or another <c>RunAsync</c>'s work, so it would
    /// wait for itself. A task an update starts without awaiting it counts too, until the update ends: start it under
    /// <see cref="ExecutionContext.SuppressFlow"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="token"/> was cancelled, or the host began to stop, before the work started.
    /// </exception>
    Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken token = default);

    /// <summary>
    /// Waits for the lock, runs <paramref name="work"/> in a DI scope of its own, releases the lock, even when the work
    /// throws, and returns the work's result.
    /// </summary>
    /// <param name="work">
    /// The work, given the scope's services and <paramref name="token"/>. Keep it short: every update waits behind it.
    /// </param>
    /// <param name="token">Cancels the wait; the work gets it too.</param>
    /// <remarks>
    /// Waiters are served in arrival order. The Bot API calls the work makes aren't linked to any update in the history.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The calling code already runs under the lock, e.g. a command or another <c>RunAsync</c>'s work, so it would
    /// wait for itself. A task an update starts without awaiting it counts too, until the update ends: start it under
    /// <see cref="ExecutionContext.SuppressFlow"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="token"/> was cancelled, or the host began to stop, before the work started.
    /// </exception>
    Task<T> RunAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken token = default);
}
