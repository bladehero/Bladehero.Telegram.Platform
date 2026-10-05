using Bladehero.Telegram.Platform.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Receiving;

// One lock for the whole bot, handed to its waiters in arrival order, so work can't starve updates.
internal sealed class TelegramLock(IServiceScopeFactory scopeFactory, IHostApplicationLifetime? lifetime = null)
    : ITelegramLock
{
    private const string Reentered =
        "This code already runs under the Telegram lock (update handling or another RunAsync), so RunAsync would "
        + "wait for itself. Do the work directly here. Work started for later must not inherit the update's context: "
        + "start it with ExecutionContext.SuppressFlow().";

    // Cancelled once the host begins to stop, so work still waiting doesn't hold up the stop.
    private readonly CancellationToken _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;

    // The lease the current flow runs under; work it starts without awaiting inherits it.
    private readonly AsyncLocal<Lease?> _current = new();

    private readonly Lock _gate = new();

    // Under _gate: the waiters, first in line first.
    private readonly LinkedList<TaskCompletionSource> _waiters = [];

    // Written under _gate.
    private bool _held;
    private int _waiting;

    // Under _gate: completed at the next change, and created only while someone watches for one.
    private TaskCompletionSource? _changed;

    public bool IsHeld => Volatile.Read(ref _held);

    public int WaitingCount => Volatile.Read(ref _waiting);

    // Completes at the next change to IsHeld or WaitingCount; read it before checking them, so no change is missed.
    internal Task Changed
    {
        get
        {
            lock (_gate)
            {
                _changed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _changed.Task;
            }
        }
    }

    public async Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await RunAsync(
            async (services, workToken) =>
            {
                await work(services, workToken);
                return true;
            },
            token
        );
    }

    public async Task<T> RunAsync<T>(
        Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(work);
        ThrowIfReentered();

        using var waitToken = CancellationTokenSource.CreateLinkedTokenSource(token, _stopping);
        await using var lease = await EnterAsync(waitToken.Token);
        _current.Value = lease;

        // Background work, even when an update started it, so its calls aren't linked to that update.
        using var noUpdate = TelegramHistoryCause.Begin(update: null);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await work(scope.ServiceProvider, token);
    }

    // Holds the lock while an update is handled. Only the update's token ends the wait, not the host stopping: a webhook
    // update is left to finish as the server drains, and a polling update's token is cancelled at stop anyway.
    internal async Task RunUpdateAsync(Func<Task> handle, CancellationToken token)
    {
        ThrowIfReentered();

        await using var lease = await EnterBeforeHandlingAsync(token);
        _current.Value = lease;
        await handle();
    }

    // Holds the lock until disposed, for the Testing package to stage races.
    internal async Task<IAsyncDisposable> HoldAsync(CancellationToken token) => await EnterAsync(token);

    // A wait cancelled here leaves the update unhandled, which the webhook answers so that Telegram sends it again.
    private async Task<Lease> EnterBeforeHandlingAsync(CancellationToken token)
    {
        try
        {
            return await EnterAsync(token);
        }
        catch (OperationCanceledException exception)
        {
            throw new WaitCanceledException(exception);
        }
    }

    private void ThrowIfReentered()
    {
        if (_current.Value is { IsReleased: false })
        {
            throw new InvalidOperationException(Reentered);
        }
    }

    private async Task<Lease> EnterAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        LinkedListNode<TaskCompletionSource> waiter;
        lock (_gate)
        {
            if (!_held)
            {
                Volatile.Write(ref _held, true);
                OnChanged();
                return new Lease(this);
            }

            waiter = _waiters.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            CountWaiters();
        }

        await using (token.Register(() => GiveUp(waiter, token)))
        {
            await waiter.Value.Task;
        }

        return new Lease(this);
    }

    // Leaves the line, unless the waiter was handed the lock first and so goes ahead with it.
    private void GiveUp(LinkedListNode<TaskCompletionSource> waiter, CancellationToken token)
    {
        lock (_gate)
        {
            if (waiter.List is null)
            {
                return;
            }

            _waiters.Remove(waiter);
            CountWaiters();
        }

        waiter.Value.TrySetCanceled(token);
    }

    // Hands the lock straight to the first waiter, so it stops counting as waiting at once and the lock stays held.
    private void Release()
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            next = _waiters.First?.Value;
            if (next is null)
            {
                Volatile.Write(ref _held, false);
                OnChanged();
            }
            else
            {
                _waiters.RemoveFirst();
                CountWaiters();
            }
        }

        next?.TrySetResult();
    }

    // Under _gate.
    private void CountWaiters()
    {
        Volatile.Write(ref _waiting, _waiters.Count);
        OnChanged();
    }

    // Under _gate.
    private void OnChanged()
    {
        _changed?.TrySetResult();
        _changed = null;
    }

    // An update's wait for the lock was cancelled, so nothing of it ran.
    internal sealed class WaitCanceledException(OperationCanceledException cancelled)
        : OperationCanceledException(
            "The update wasn't handled: its wait for the Telegram lock was cancelled.",
            cancelled,
            cancelled.CancellationToken
        );

    // Releases the lock once. Flows that inherited it stop counting as holders then.
    private sealed class Lease(TelegramLock owner) : IAsyncDisposable
    {
        private int _released;

        public bool IsReleased => Volatile.Read(ref _released) == 1;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
