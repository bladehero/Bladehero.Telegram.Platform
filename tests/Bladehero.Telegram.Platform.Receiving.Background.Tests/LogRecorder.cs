using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

// Keeps each entry with the TelegramUpdateId in scope, if any.
internal sealed class LogRecorder : ILoggerProvider, ISupportExternalScope
{
    private readonly List<(LogLevel Level, Exception? Exception, long? UpdateId)> _entries = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<(LogLevel Level, Exception? Exception, long? UpdateId)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    private long? UpdateId()
    {
        long? updateId = null;
        _scopes.ForEachScope(
            (scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        if (key == "TelegramUpdateId" && value is int or long)
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

    private sealed class Logger(LogRecorder recorder) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => recorder._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var updateId = recorder.UpdateId();
            lock (recorder._entries)
            {
                recorder._entries.Add((logLevel, exception, updateId));
            }
        }
    }
}
