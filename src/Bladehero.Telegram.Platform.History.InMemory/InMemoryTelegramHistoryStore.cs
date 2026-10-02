namespace Bladehero.Telegram.Platform.History.InMemory;

// The latest entries of each chat; reads may run while the writer appends.
internal sealed class InMemoryTelegramHistoryStore(int maxEntriesPerChat) : ITelegramHistoryStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, Queue<TelegramHistoryEntry>> _chats = [];

    // Entries without a chat, such as inline queries, share one bucket and its cap.
    private readonly Queue<TelegramHistoryEntry> _withoutChat = new();
    private long _lastId;

    public Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
    {
        lock (_gate)
        {
            foreach (var entry in entries)
            {
                var bucket = BucketOf(entry.ChatId);
                bucket.Enqueue(entry with { Id = ++_lastId });
                if (bucket.Count > maxEntriesPerChat)
                {
                    bucket.Dequeue();
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(TelegramHistoryQuery query, CancellationToken token)
    {
        lock (_gate)
        {
            IEnumerable<TelegramHistoryEntry> candidates = query.ChatId is { } chatId
                ? _chats.GetValueOrDefault(chatId) ?? []
                : _chats.Values.SelectMany(x => x).Concat(_withoutChat);

            IReadOnlyList<TelegramHistoryEntry> latest =
            [
                .. candidates
                    .Where(x => Matches(x, query))
                    .OrderByDescending(x => x.Id)
                    .Take(query.Limit)
                    .OrderBy(x => x.Id),
            ];
            return Task.FromResult(latest);
        }
    }

    private Queue<TelegramHistoryEntry> BucketOf(long? chatId)
    {
        if (chatId is not { } id)
        {
            return _withoutChat;
        }

        if (!_chats.TryGetValue(id, out var bucket))
        {
            _chats[id] = bucket = new Queue<TelegramHistoryEntry>();
        }

        return bucket;
    }

    private static bool Matches(TelegramHistoryEntry entry, TelegramHistoryQuery query) =>
        (query.MessageId is null || entry.MessageId == query.MessageId)
        && (query.InlineMessageId is null || entry.InlineMessageId == query.InlineMessageId)
        && (query.UpdateId is null || entry.UpdateId == query.UpdateId)
        && (query.Since is null || entry.Time >= query.Since)
        && (query.BeforeId is null || entry.Id < query.BeforeId);
}
