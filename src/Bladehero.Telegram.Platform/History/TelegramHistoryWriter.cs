using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.History;

// Stores recorded entries in the background, in batches, so the bot never waits for the store nor sees it fail.
internal sealed class TelegramHistoryWriter(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramHistoryOptions> options,
    ILogger<TelegramHistoryWriter> logger,
    TimeProvider? timeProvider = null
) : IHostedLifecycleService, IDisposable
{
    internal const int BatchSize = 100;
    private static readonly TimeSpan DropWarningInterval = TimeSpan.FromMinutes(1);

    private readonly int _capacity = options.Value.QueueCapacity;
    private readonly Func<TelegramHistoryEntry, TelegramHistoryEntry?>? _filter = options.Value.Filter;

    // The app's clock when it registered one; the library registers none.
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Entries, and flush markers in the same order.
    private readonly Channel<Item> _queue = Channel.CreateBounded<Item>(options.Value.QueueCapacity);

    // Cancelled when shutdown gives up on what's left.
    private readonly CancellationTokenSource _abandoned = new();
    private readonly Lock _gate = new();
    private volatile Task? _loop;
    private volatile bool _stopped;
    private volatile bool _failing;

    // Recorded and not yet handed to the store.
    private int _unstored;
    private int _dropped;
    private DateTimeOffset? _lastDropWarning;
    private ITimer? _dropReport;

    // Whether the last batch failed to be stored.
    public bool Failing => _failing;

    // Queues the entry; never waits nor throws. False when it was dropped.
    public bool Record(TelegramHistoryEntry entry)
    {
        Interlocked.Increment(ref _unstored);
        if (_queue.Writer.TryWrite(new Item(entry, null)))
        {
            return true;
        }

        Interlocked.Decrement(ref _unstored);
        if (!_stopped)
        {
            Dropped();
        }

        return false;
    }

    // Completes once every entry queued before it was handed to the store; at once when the writer isn't running.
    public async Task FlushAsync(CancellationToken token)
    {
        if (_loop is null)
        {
            return;
        }

        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Waits for room rather than being dropped.
            await _queue.Writer.WriteAsync(new Item(null, flushed), token);
        }
        catch (ChannelClosedException)
        {
            return;
        }

        await flushed.Task.WaitAsync(token);
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Not tied to the start's token: the loop runs until the queue is completed at shutdown.
        _loop ??= Task.Run(RunAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // After every hosted service's StopAsync, so the last updates' entries are in; bounded by the shutdown timeout.
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        _queue.Writer.TryComplete();
        ReportDrops();
        if (_loop is { } loop)
        {
            try
            {
                await loop.WaitAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        // Given up on what's queued or being stored; counted first, as cancelling may end the batch in flight at once.
        var left = Volatile.Read(ref _unstored);
        _abandoned.Cancel();
        while (_queue.Reader.TryRead(out var item))
        {
            item.Flushed?.TrySetResult();
        }

        if (left > 0)
        {
            logger.LogWarning("The Telegram history wasn't fully stored at shutdown: {Count} left.", left);
        }
    }

    // A host disposed without stopping leaves no loop behind, and later entries are ignored.
    public void Dispose()
    {
        _stopped = true;
        _queue.Writer.TryComplete();
        _abandoned.Cancel();
        while (_queue.Reader.TryRead(out var item))
        {
            item.Flushed?.TrySetResult();
        }

        lock (_gate)
        {
            _dropReport?.Dispose();
            _dropReport = null;
        }
    }

    private async Task RunAsync()
    {
        while (await _queue.Reader.WaitToReadAsync())
        {
            var batch = new List<TelegramHistoryEntry>();
            var taken = 0;
            TaskCompletionSource? flushed = null;
            while (taken < BatchSize && !_abandoned.IsCancellationRequested && _queue.Reader.TryRead(out var item))
            {
                if (item.Entry is not { } entry)
                {
                    flushed = item.Flushed;
                    break;
                }

                taken++;
                if (Filter(entry) is { } kept)
                {
                    batch.Add(kept);
                }
            }

            if (batch.Count > 0)
            {
                await StoreAsync(batch);
            }

            Interlocked.Add(ref _unstored, -taken);
            flushed?.TrySetResult();
            if (_abandoned.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private TelegramHistoryEntry? Filter(TelegramHistoryEntry entry)
    {
        if (_filter is null)
        {
            return entry;
        }

        try
        {
            return _filter(entry);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The Telegram history filter failed on a {Kind} entry; it was dropped.",
                entry.Kind
            );
            return null;
        }
    }

    private async Task StoreAsync(List<TelegramHistoryEntry> batch)
    {
        try
        {
            // A scope per batch, so a scoped store, such as a DbContext, is the writer's alone.
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ITelegramHistoryStore>();
            await store.AppendAsync(batch, _abandoned.Token);
            _failing = false;
        }
        catch (Exception exception)
        {
            // Once shutdown gave up, what's left was reported already.
            if (!_abandoned.IsCancellationRequested)
            {
                _failing = true;
                logger.LogError(
                    exception,
                    "The Telegram history store failed; a batch of {Count} was dropped.",
                    batch.Count
                );
            }
        }
    }

    // A warning on the first drop, then at most once a minute; drops within the minute are reported when it's up.
    private void Dropped()
    {
        int dropped;
        lock (_gate)
        {
            _dropped++;
            var now = _time.GetUtcNow();
            if (_lastDropWarning is { } last && now - last < DropWarningInterval)
            {
                _dropReport ??= ReportDropsIn(last + DropWarningInterval - now);
                return;
            }

            _dropReport?.Dispose();
            _dropReport = null;
            _lastDropWarning = now;
            dropped = _dropped;
            _dropped = 0;
        }

        WarnOfDrops(dropped);
    }

    // Without the dropping caller's context: the report belongs to no update and mustn't keep one alive.
    private ITimer ReportDropsIn(TimeSpan due)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            return _time.CreateTimer(_ => ReportDrops(), null, due, Timeout.InfiniteTimeSpan);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return _time.CreateTimer(_ => ReportDrops(), null, due, Timeout.InfiniteTimeSpan);
        }
    }

    // The drops not yet reported, if any.
    private void ReportDrops()
    {
        int dropped;
        lock (_gate)
        {
            _dropReport?.Dispose();
            _dropReport = null;
            if (_dropped == 0)
            {
                return;
            }

            _lastDropWarning = _time.GetUtcNow();
            dropped = _dropped;
            _dropped = 0;
        }

        WarnOfDrops(dropped);
    }

    private void WarnOfDrops(int dropped) =>
        logger.LogWarning(
            "The Telegram history queue is full ({Capacity} entries): {Count} dropped.",
            _capacity,
            dropped
        );

    // An entry, or a flush marker.
    private readonly record struct Item(TelegramHistoryEntry? Entry, TaskCompletionSource? Flushed);
}
