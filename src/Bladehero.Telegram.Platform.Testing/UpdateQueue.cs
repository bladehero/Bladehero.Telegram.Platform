using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Serves updates to the bot's polling loop the way getUpdates does. An update counts as handled once the loop asks
// for an offset past it: the loop only moves its offset on after it has finished with the update, which is the same
// signal Telegram uses to forget an update.
internal sealed class UpdateQueue
{
    private const int DefaultLimit = 100;

    private readonly object _gate = new();
    private readonly List<(int Id, JsonObject Update)> _pending = [];
    private readonly Dictionary<int, TaskCompletionSource> _handled = [];
    private TaskCompletionSource _arrived = NewSignal();
    private int _lastId;

    public int Add(JsonObject update)
    {
        var json = update.DeepClone().AsObject();

        lock (_gate)
        {
            var id = ++_lastId;
            json["update_id"] = id;
            _pending.Add((id, json));
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

    public async Task<JsonArray> TakeAsync(JsonObject parameters, CancellationToken token)
    {
        var offset = parameters["offset"]?.GetValue<long>() ?? 0;
        var limit = parameters["limit"]?.GetValue<int>() ?? DefaultLimit;
        var timeout = TimeSpan.FromSeconds(parameters["timeout"]?.GetValue<int>() ?? 0);

        while (true)
        {
            Task arrived;
            lock (_gate)
            {
                ConfirmBelow(offset);

                var ready = offset < 0 ? _pending.TakeLast((int)-offset) : _pending.Where(x => x.Id >= offset);
                var batch = ready.Take(limit).Select(x => (JsonNode)x.Update.DeepClone()).ToArray();
                if (batch.Length > 0 || timeout <= TimeSpan.Zero)
                {
                    return new JsonArray(batch);
                }

                arrived = _arrived.Task;
            }

            try
            {
                await arrived.WaitAsync(timeout, token);
            }
            catch (TimeoutException)
            {
                return [];
            }
        }
    }

    private void ConfirmBelow(long offset)
    {
        foreach (var (id, _) in _pending.Where(x => x.Id < offset).ToArray())
        {
            _pending.RemoveAll(x => x.Id == id);
            _handled.Remove(id, out var handled);
            handled?.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
