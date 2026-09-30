using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

public sealed class ReceivingUpdateHandlerTests
{
    [Fact]
    public async Task HandleUpdateAsync_ShouldLogWithinAScopeCarryingTheUpdateId()
    {
        // Arrange
        var logger = new ScopeRecordingLogger();
        object? scopeWhileHandling = null;
        var executor = new Mock<ITelegramCommandExecutor>();
        executor
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => scopeWhileHandling = logger.Scope)
            .Returns(Task.CompletedTask);
        var sut = new ReceivingUpdateHandler(
            executor.Object,
            Mock.Of<ITelegramErrorHandler>(),
            new Conversation(new InMemoryConversationStore()),
            logger
        );

        // Act
        await sut.HandleUpdateAsync(Mock.Of<ITelegramBotClient>(), new Update { Id = 7 }, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            scopeWhileHandling.Should().BeEquivalentTo(new Dictionary<string, object> { ["TelegramUpdateId"] = 7 });
            logger.Scope.Should().BeNull();
        }
    }

    private sealed class ScopeRecordingLogger : ILogger<ReceivingUpdateHandler>
    {
        public object? Scope { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            Scope = state;
            return new EndScope(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }

        private sealed class EndScope(ScopeRecordingLogger logger) : IDisposable
        {
            public void Dispose() => logger.Scope = null;
        }
    }
}
