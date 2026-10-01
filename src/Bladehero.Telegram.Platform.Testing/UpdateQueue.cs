using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// getUpdates for the polling loop. An update is handled once the loop asks for an offset past it, the signal Telegram
// uses too.
internal sealed class UpdateQueue
{
    private const int DefaultLimit = 100;

    private readonly object _gate = new();
    private readonly List<Pending> _pending = [];
    private readonly Dictionary<int, TaskCompletionSource> _handled = [];
    private TaskCompletionSource _arrived = NewSignal();
    private TaskCompletionSource _polled = NewSignal();

    // The waiting poll's signal that a newer poll ended it, as Telegram ends the older of two.
    private TaskCompletionSource? _waiting;
    private int _lastId;
    private int _polls;
    private int _inFlight;
    private int _superseded;

    // getUpdates calls with an offset of 0 or more: those of a running loop, not the one that drops pending updates.
    public int Polls
    {
        get
        {
            lock (_gate)
            {
                return _polls;
            }
        }
    }

    public int InFlight => Volatile.Read(ref _inFlight);

    // Whether a poll was ever ended by a newer one.
    public bool Superseded => Volatile.Read(ref _superseded) > 0;

    // Whether a poll waits for updates now.
    public bool Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting is not null;
            }
        }
    }

    public int Add(JsonObject update)
    {
        var json = update.DeepClone().AsObject();

        lock (_gate)
        {
            var id = ++_lastId;
            json["update_id"] = id;
            _pending.Add(new Pending(id, json));
            _handled[id] = NewSignal();

            _arrived.TrySetResult();
            _arrived = NewSignal();

            return id;
        }
    }

    public Task HandledAsync(int updateId)
    {
        lock (_gate)
        {
            return _handled.TryGetValue(updateId, out var handled) ? handled.Task : Task.CompletedTask;
        }
    }

    // Completes once there have been more than `polls` polls.
    public Task PolledAsync(int polls)
    {
        lock (_gate)
        {
            return _polls > polls ? Task.CompletedTask : _polled.Task;
        }
    }

    // Whether the loop fetched an update it has not finished, and whether it is still busy with earlier ones.
    public (bool Fetched, bool BusyBefore) Progress(int updateId)
    {
        lock (_gate)
        {
            return (_pending.Any(x => x.Id == updateId && x.Fetched), _pending.Any(x => x.Id < updateId && x.Fetched));
        }
    }

    // Null when a newer poll that has to wait ends this one; a poll that returns at once ends none.
    public async Task<JsonArray?> TakeAsync(JsonObject parameters, CancellationToken token)
    {
        var offset = parameters["offset"]?.GetValue<long>() ?? 0;
        var limit = parameters["limit"]?.GetValue<int>() ?? DefaultLimit;
        var timeout = TimeSpan.FromSeconds(parameters["timeout"]?.GetValue<int>() ?? 0);
        TaskCompletionSource? superseded = null;

        Interlocked.Increment(ref _inFlight);
        try
        {
            if (offset >= 0)
            {
                lock (_gate)
                {
                    _polls++;
                    _polled.TrySetResult();
                    _polled = NewSignal();
                }
            }

            while (true)
            {
                Task arrived;
                lock (_gate)
                {
                    ConfirmBelow(offset);

                    var ready = offset < 0 ? _pending.TakeLast((int)-offset) : _pending.Where(x => x.Id >= offset);
                    var batch = ready.Take(limit).ToArray();
                    if (batch.Length > 0 || timeout <= TimeSpan.Zero)
                    {
                        foreach (var pending in batch)
                        {
                            pending.Fetched = true;
                        }

                        return new JsonArray([.. batch.Select(x => (JsonNode)x.Update.DeepClone())]);
                    }

                    arrived = _arrived.Task;

                    if (superseded is null)
                    {
                        _waiting?.TrySetResult();
                        _waiting = superseded = NewSignal();
                    }
                }

                try
                {
                    await Task.WhenAny(arrived, superseded.Task).WaitAsync(timeout, token);
                }
                catch (TimeoutException)
                {
                    return [];
                }

                if (superseded.Task.IsCompleted)
                {
                    Interlocked.Increment(ref _superseded);
                    return null;
                }
            }
        }
        finally
        {
            // A poll that ends, cancelled included, no longer waits, so a later one is no conflict.
            lock (_gate)
            {
                if (_waiting == superseded)
                {
                    _waiting = null;
                }
            }

            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void ConfirmBelow(long offset)
    {
        foreach (var pending in _pending.Where(x => x.Id < offset).ToArray())
        {
            _pending.Remove(pending);
            _handled.Remove(pending.Id, out var handled);
            handled?.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Pending(int id, JsonObject update)
    {
        public int Id { get; } = id;

        public JsonObject Update { get; } = update;

        public bool Fetched { get; set; }
    }
}
