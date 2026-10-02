using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Tests;

// Keeps each entry with the log scopes it was written in, if any.
internal sealed class LogRecorder : ILoggerProvider, ISupportExternalScope
{
    private readonly List<(LogLevel Level, string Message, string? Scope)> _entries = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<(LogLevel Level, string Message, string? Scope)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IReadOnlyList<string> At(LogLevel level) => [.. Entries.Where(x => x.Level == level).Select(x => x.Message)];

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    private string? Scope()
    {
        List<string> scopes = [];
        _scopes.ForEachScope((scope, list) => list.Add(scope?.ToString() ?? ""), scopes);
        return scopes.Count == 0 ? null : string.Join(" > ", scopes);
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
            var scope = recorder.Scope();
            lock (recorder._entries)
            {
                recorder._entries.Add((logLevel, formatter(state, exception), scope));
            }
        }
    }
}
