using Bladehero.Telegram.Platform.History;

namespace Bladehero.Telegram.Platform.Tests.History;

// Keeps each batch; Close makes AppendAsync wait until Open, and Failure makes it throw.
internal sealed class RecordingStore : ITelegramHistoryStore
{
    private readonly List<TelegramHistoryEntry[]> _batches = [];
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _gate;
    private long _lastId;

    public Exception? Failure { get; set; }

    // Completes when AppendAsync is first called.
    public Task Entered => _entered.Task;

    public IReadOnlyList<TelegramHistoryEntry[]> Batches
    {
        get
        {
            lock (_batches)
            {
                return [.. _batches];
            }
        }
    }

    public IReadOnlyList<TelegramHistoryEntry> Entries => [.. Batches.SelectMany(x => x)];

    public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open() => _gate?.TrySetResult();

    public async Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
    {
        _entered.TrySetResult();
        if (_gate is { } gate)
        {
            await gate.Task.WaitAsync(token);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        lock (_batches)
        {
            _batches.Add([.. entries.Select(x => x with { Id = ++_lastId })]);
        }
    }

    public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(TelegramHistoryQuery query, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<TelegramHistoryEntry>>([
            .. Entries.Where(x => query.ChatId is null || x.ChatId == query.ChatId).TakeLast(query.Limit),
        ]);
}
