using Bladehero.Telegram.Platform.History;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

// Keeps the history in a list, in the order it was stored.
internal sealed class HistoryStore : ITelegramHistoryStore
{
    private readonly List<TelegramHistoryEntry> _entries = [];

    public IReadOnlyList<TelegramHistoryEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
    {
        lock (_entries)
        {
            _entries.AddRange(entries);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(TelegramHistoryQuery query, CancellationToken token) =>
        Task.FromResult(Entries);
}
