using Bladehero.Telegram.Platform.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Receiving;

// One lock for the whole bot. SemaphoreSlim serves its async waiters in arrival order, so work can't starve updates.
internal sealed class TelegramLock(IServiceScopeFactory scopeFactory, IHostApplicationLifetime? lifetime = null)
    : ITelegramLock
{
    private const string Reentered =
        "This code already runs under the Telegram lock (update handling or another RunAsync), so RunAsync would "
        + "wait for itself. Do the work directly here. Work started for later must not inherit the update's context: "
        + "start it with ExecutionContext.SuppressFlow().";

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Cancelled once the host begins to stop, so work still waiting doesn't hold up the stop.
    private readonly CancellationToken _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;

    // The lease the current flow runs under; work it starts without awaiting inherits it.
    private readonly AsyncLocal<Lease?> _current = new();

    private int _waiting;
    private TaskCompletionSource _changed = NewSignal();

    public bool IsHeld => _gate.CurrentCount == 0;

    public int WaitingCount => Volatile.Read(ref _waiting);

    // Completes at the next change to IsHeld or WaitingCount; read it before checking them, so no change is missed.
    internal Task Changed => Volatile.Read(ref _changed).Task;

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

    // Holds the lock while an update is handled. Only the update's token ends the wait: at shutdown, a webhook update
    // cancelled here would still be answered 200 and lost, and a polling update's token is cancelled anyway.
    internal async Task RunUpdateAsync(Func<Task> handle, CancellationToken token)
    {
        ThrowIfReentered();

        await using var lease = await EnterAsync(token);
        _current.Value = lease;
        await handle();
    }

    // Holds the lock until disposed, for the Testing package to stage races.
    internal async Task<IAsyncDisposable> HoldAsync(CancellationToken token) => await EnterAsync(token);

    private void ThrowIfReentered()
    {
        if (_current.Value is { IsReleased: false })
        {
            throw new InvalidOperationException(Reentered);
        }
    }

    private async Task<Lease> EnterAsync(CancellationToken token)
    {
        if (!_gate.Wait(0, token))
        {
            await WaitInLineAsync(token);
        }

        OnChanged();
        return new Lease(this);
    }

    // Counted in WaitingCount until it has the lock or gives up.
    private async Task WaitInLineAsync(CancellationToken token)
    {
        Interlocked.Increment(ref _waiting);
        OnChanged();
        try
        {
            await _gate.WaitAsync(token);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
            OnChanged();
        }
    }

    private void Release()
    {
        _gate.Release();
        OnChanged();
    }

    private void OnChanged() => Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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
