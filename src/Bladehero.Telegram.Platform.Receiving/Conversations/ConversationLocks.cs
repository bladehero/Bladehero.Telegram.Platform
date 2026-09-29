namespace Bladehero.Telegram.Platform.Receiving.Conversations;

// Serializes bound taps on one conversation within this process, so two taps at once, as concurrent webhook requests
// bring them, see the conversation one after the other. A key is forgotten once nobody holds or awaits it.
internal sealed class ConversationLocks
{
    private readonly Dictionary<ConversationKey, Entry> _entries = [];

    // For tests: how many keys are held or awaited.
    internal int Count
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    // For tests: how many hold or await the key.
    internal int UsersOf(ConversationKey key)
    {
        lock (_entries)
        {
            return _entries.GetValueOrDefault(key)?.Users ?? 0;
        }
    }

    public async ValueTask<IAsyncDisposable> EnterAsync(ConversationKey key, CancellationToken token)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                _entries[key] = entry = new Entry();
            }

            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(token);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Leave(ConversationKey key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Lease(ConversationLocks locks, ConversationKey key, Entry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                entry.Gate.Release();
                locks.Leave(key, entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
