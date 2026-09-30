using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
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
            Mock.Of<ITelegramBotIdentity>(),
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

    [Fact]
    public async Task HandleUpdateAsync_ShouldMakeTheBotsUsernameKnownToIsCommand()
    {
        // Arrange
        var identity = Mock.Of<ITelegramBotIdentity>(x => x.Current == new User { Username = "test_bot" });
        var (sut, seen) = HandlerSeeingStart(identity);

        // Act
        await sut.HandleUpdateAsync(Mock.Of<ITelegramBotClient>(), Text("/start@other_bot"), CancellationToken.None);

        // Assert
        seen().Should().BeFalse();
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenGetMeFails_ShouldStillHandleTheUpdateLeniently()
    {
        // Arrange
        var client = new Mock<ITelegramBotClient>();
        client
            .Setup(x => x.SendRequest(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var (sut, seen) = HandlerSeeingStart(new TelegramBotIdentity(client.Object));

        // Act
        await sut.HandleUpdateAsync(client.Object, Text("/start@other_bot"), CancellationToken.None);

        // Assert
        seen().Should().BeTrue();
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenTheUsernameIsUnknown_ShouldNotWaitForGetMe()
    {
        // Arrange: getMe hangs until the test lets it go.
        var getMe = new TaskCompletionSource<User>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<ITelegramBotClient>();
        client.Setup(x => x.SendRequest(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>())).Returns(getMe.Task);
        var (sut, seen) = HandlerSeeingStart(new TelegramBotIdentity(client.Object));

        // Act
        await sut.HandleUpdateAsync(client.Object, Text("/start@other_bot"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        seen().Should().BeTrue();
        getMe.SetResult(new User { Username = "test_bot" });
    }

    [Fact]
    public async Task HandleUpdateAsync_OnceGetMeSucceeds_ShouldRefuseCommandsForOtherBots()
    {
        // Arrange: the first update starts getMe.
        var client = new Mock<ITelegramBotClient>();
        client
            .Setup(x => x.SendRequest(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Username = "test_bot" });
        var identity = new TelegramBotIdentity(client.Object);
        var (sut, seen) = HandlerSeeingStart(identity);
        await sut.HandleUpdateAsync(client.Object, Text("/start@other_bot"), CancellationToken.None);
        await identity.GetAsync(CancellationToken.None);

        // Act
        await sut.HandleUpdateAsync(client.Object, Text("/start@other_bot"), CancellationToken.None);

        // Assert
        seen().Should().BeFalse();
    }

    // A handler whose executor records whether it saw the update's message as /start.
    private static (ReceivingUpdateHandler Handler, Func<bool?> Seen) HandlerSeeingStart(ITelegramBotIdentity identity)
    {
        bool? seen = null;
        var executor = new Mock<ITelegramCommandExecutor>();
        executor
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()))
            .Callback(
                (CommandRequest request, CancellationToken _) => seen = request.Update.Message!.IsCommand("/start")
            )
            .Returns(Task.CompletedTask);

        var handler = new ReceivingUpdateHandler(
            executor.Object,
            Mock.Of<ITelegramErrorHandler>(),
            new Conversation(new InMemoryConversationStore()),
            identity,
            NullLogger<ReceivingUpdateHandler>.Instance
        );

        return (handler, () => seen);
    }

    private static Update Text(string text) =>
        new()
        {
            Id = 1,
            Message = new Message
            {
                Text = text,
                Chat = new Chat { Id = 42 },
                From = new User { Id = 42 },
            },
        };

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
