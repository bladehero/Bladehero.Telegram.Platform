using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

internal sealed class LogRecorder : ILoggerProvider
{
    private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries
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
                recorder._entries.Add((logLevel, exception));
            }
        }
    }
}
