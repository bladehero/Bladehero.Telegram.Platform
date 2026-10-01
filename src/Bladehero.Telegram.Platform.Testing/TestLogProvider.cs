using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Testing;

// Keeps every entry, with the update id from the TelegramUpdateId scope the library opens per update.
internal sealed class TestLogProvider : ILoggerProvider, ISupportExternalScope
{
    private const string UpdateIdScope = "TelegramUpdateId";

    private readonly List<TestLog> _entries = [];

    // Entries an action already failed for, by index.
    private readonly HashSet<int> _claimed = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<TestLog> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    // The unclaimed entries that match, now claimed.
    public List<TestLog> Claim(Func<TestLog, bool> match)
    {
        lock (_entries)
        {
            var claimed = new List<TestLog>();
            for (var index = 0; index < _entries.Count; index++)
            {
                if (!_claimed.Contains(index) && match(_entries[index]))
                {
                    _claimed.Add(index);
                    claimed.Add(_entries[index]);
                }
            }

            return claimed;
        }
    }

    public void Dispose() { }

    private void Add(TestLog entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    // The innermost update id in scope.
    private long? UpdateId()
    {
        long? updateId = null;
        _scopes.ForEachScope(
            (scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        if (key == UpdateIdScope && value is int or long)
                        {
                            updateId = Convert.ToInt64(value);
                        }
                    }
                }
            },
            (object?)null
        );

        return updateId;
    }

    private sealed class Logger(TestLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (IsEnabled(logLevel))
            {
                var message = formatter(state, exception);
                provider.Add(new TestLog(logLevel, category, eventId, message, exception, provider.UpdateId()));
            }
        }
    }
}
