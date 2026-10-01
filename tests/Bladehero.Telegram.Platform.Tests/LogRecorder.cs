using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Tests;

internal sealed class LogRecorder : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
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

    public void Dispose() { }

    private sealed class Logger(LogRecorder recorder) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (recorder._entries)
            {
                recorder._entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
