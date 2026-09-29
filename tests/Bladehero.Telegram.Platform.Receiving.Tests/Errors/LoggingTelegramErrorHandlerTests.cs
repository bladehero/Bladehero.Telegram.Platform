using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Errors;

public sealed class LoggingTelegramErrorHandlerTests
{
    private static readonly ITelegramBotClient Client = new TelegramBotClient(
        "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"
    );

    [Fact]
    public async Task HandleAsync_ATimedOutRequest_ShouldBeLoggedAsAnError()
    {
        // Arrange
        var logger = new RecordingLogger();
        var sut = new LoggingTelegramErrorHandler(logger);
        var timeout = new RequestException("Request timed out", new TaskCanceledException());

        // Act
        await sut.HandleAsync(new TelegramError(timeout, Client, new Update { Id = 7 }));

        // Assert
        logger.Entries.Should().Equal((LogLevel.Error, timeout));
    }

    [Fact]
    public async Task HandleAsync_APollingError_ShouldBeLoggedAsAWarning()
    {
        // Arrange
        var logger = new RecordingLogger();
        var sut = new LoggingTelegramErrorHandler(logger);
        var unreachable = new RequestException("Telegram is unreachable", new HttpRequestException());

        // Act
        await sut.HandleAsync(new TelegramError(unreachable, Client));

        // Assert
        logger.Entries.Should().Equal((LogLevel.Warning, unreachable));
    }

    private sealed class RecordingLogger : ILogger<LoggingTelegramErrorHandler>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, exception));
    }
}
