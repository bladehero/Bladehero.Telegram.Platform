using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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

    [Fact]
    public async Task HandleUpdateAsync_WithHistory_ShouldRecordTheUpdateBeforeAnyCommandRuns()
    {
        // Arrange
        var store = new ListStore();
        await using var history = await StartedHistoryAsync(store);
        IReadOnlyList<TelegramHistoryEntry>? recordedBeforeTheCommand = null;
        var executor = new Mock<ITelegramCommandExecutor>();
        executor
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()))
            .Returns(
                async (CommandRequest _, CancellationToken token) =>
                {
                    await history.Writer.FlushAsync(token);
                    recordedBeforeTheCommand = store.Entries;
                }
            );
        var sut = Handler(executor.Object, history.Writer);

        // Act
        await sut.HandleUpdateAsync(Mock.Of<ITelegramBotClient>(), Coffee(), CancellationToken.None);

        // Assert
        recordedBeforeTheCommand
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "message",
                    UpdateId = 1,
                    Text = "/coffee",
                }
            );
    }

    [Fact]
    public async Task HandleUpdateAsync_ShouldMakeTheUpdateTheCauseOfCallsWhileHandling()
    {
        // Arrange
        Update? causeWhileHandling = null;
        var executor = new Mock<ITelegramCommandExecutor>();
        executor
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => causeWhileHandling = TelegramHistoryCause.Current)
            .Returns(Task.CompletedTask);
        var sut = Handler(executor.Object);
        var update = Coffee();

        // Act
        await sut.HandleUpdateAsync(Mock.Of<ITelegramBotClient>(), update, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            causeWhileHandling.Should().BeSameAs(update);
            TelegramHistoryCause.Current.Should().BeNull();
        }
    }

    [Fact]
    public async Task HandleUpdateAsync_WithoutHistory_ShouldHandleTheUpdate()
    {
        // Arrange
        var executor = new Mock<ITelegramCommandExecutor>();
        var sut = Handler(executor.Object);

        // Act
        await sut.HandleUpdateAsync(Mock.Of<ITelegramBotClient>(), Coffee(), CancellationToken.None);

        // Assert
        executor.Verify(x => x.ExecuteAsync(It.IsAny<CommandRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReceivingUpdateHandler Handler(
        ITelegramCommandExecutor executor,
        TelegramHistoryWriter? history = null
    ) =>
        new(
            executor,
            Mock.Of<ITelegramErrorHandler>(),
            new Conversation(new InMemoryConversationStore()),
            Mock.Of<ITelegramBotIdentity>(),
            NullLogger<ReceivingUpdateHandler>.Instance,
            history
        );

    // A running history writer over the store; disposing it stops the writer.
    private static async Task<StartedHistory> StartedHistoryAsync(ITelegramHistoryStore store)
    {
        var services = new ServiceCollection().AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddTelegramHistory().Services.AddSingleton(store);
        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<TelegramHistoryWriter>();
        await writer.StartedAsync(CancellationToken.None);
        return new StartedHistory(provider, writer);
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

    // A whole update, as Telegram sends it, so the history can serialize it.
    private static Update Coffee() =>
        new()
        {
            Id = 1,
            Message = new Message
            {
                Id = 5,
                Date = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
                Text = "/coffee",
                Chat = new Chat { Id = 7000000001, Type = ChatType.Private },
                From = new User { Id = 7000000001, FirstName = "Nick" },
            },
        };

    private sealed record StartedHistory(ServiceProvider Provider, TelegramHistoryWriter Writer) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Writer.StoppedAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private sealed class ListStore : ITelegramHistoryStore
    {
        private readonly List<TelegramHistoryEntry> _entries = [];

        public IReadOnlyList<TelegramHistoryEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
        {
            lock (_entries)
            {
                _entries.AddRange(entries);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
            TelegramHistoryQuery query,
            CancellationToken token
        ) => throw new NotSupportedException();
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
