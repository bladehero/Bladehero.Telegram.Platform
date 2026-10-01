using System.Reflection;
using System.Text.Json;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationAwareCommandExecutorTests
{
    private const string Flow = "order";
    private const string Size = "size";
    private const string Name = "name";

    private const long ChatId = 1;
    private const long UserId = 7;
    private const long OtherUserId = 8;

    private static readonly ConversationKey Sender = new(ChatId, UserId);

    [Fact]
    public async Task ExecuteAsync_WhenNoConversationIsActive_ShouldRunRegularCommandsOnly()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.SendAsync("hello");

        // Assert
        bot.Journal.Entries.Should().Equal("echo");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAStepHandlesTheUpdate_ShouldSkipRegularCommands()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size);

        // Act
        await bot.SendAsync("large");

        // Assert
        bot.Journal.Entries.Should().Equal("size");
    }

    [Fact]
    public async Task ExecuteAsync_WhenEveryStepDeclines_ShouldFallThroughToRegularCommands()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Name);

        // Act
        await bot.SendAsync("/cancel");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("cancel");
            (await bot.CurrentAsync()).Should().BeNull();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenConversationIsAtAnotherStep_ShouldNotRunThatStep()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Name);

        // Act
        await bot.SendAsync("Nick");

        // Assert
        bot.Journal.Entries.Should().Equal("name");
    }

    [Theory]
    [InlineData(Size)]
    [InlineData(Name)]
    public async Task ExecuteAsync_WhenStepHasNoStepName_ShouldRunAtEveryStepOfTheFlow(string step)
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(step);

        // Act
        await bot.TapAsync("back");

        // Assert
        bot.Journal.Entries.Should().Equal("back");
    }

    [Fact]
    public async Task ExecuteAsync_WhenConversationIsInAnotherFlow_ShouldRunRegularCommandsOnly()
    {
        // Arrange
        var bot = new Bot();
        await bot.Store.SaveAsync(Sender, new ConversationState("survey", Size), CancellationToken.None);

        // Act
        await bot.SendAsync("large");

        // Assert
        bot.Journal.Entries.Should().Equal("echo");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoStepCommandsExist_ShouldNeverReadTheStore()
    {
        // Arrange
        var store = new Mock<IConversationStore>();
        var bot = new Bot(store.Object, withSteps: false);

        // Act
        await bot.SendAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("echo");
            store.Verify(x => x.GetAsync(It.IsAny<ConversationKey>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InAGroup_ShouldNotLetOneMemberAdvanceAnothersConversation()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size);

        // Act
        await bot.SendAsync("large", OtherUserId);

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("echo");
            (await bot.CurrentAsync()).Should().Be(new ConversationState(Flow, Size));
        }
    }

    [Fact]
    public async Task ExecuteAsync_AcrossUpdates_ShouldRouteEachByTheStateThePreviousOneLeft()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.SendAsync("/order");
        await bot.SendAsync("large");
        await bot.SendAsync("Nick");
        await bot.SendAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            // "large" moves the conversation to the name step, yet only reaches the size step: a change steers
            // the next update, not the one making it.
            bot.Journal.Entries.Should().Equal("start", "size", "name", "echo");
            (await bot.CurrentAsync()).Should().BeNull();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoCommandTakesATypedButton_ShouldRefuseItAsUnclaimed()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync("exec-cup:3");

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.Unclaimed, typeof(Cup), "exec-cup:3"));
            bot.Journal.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenATypedButtonNoLongerDecodes_ShouldRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync("exec-cup:large");

        // Assert
        bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), "exec-cup:large"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenARegularCommandTakesATypedButton_ShouldNotRefuseIt()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync("exec-cup:1");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("cup");
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapOnTheCurrentRun_ShouldRunTheStep()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run1");

        // Act
        await bot.TapAsync(Bound("exec-cup:2", UserId, "run1"));

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("cup at the size step");
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapAfterTheConversationEnded_ShouldRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot();
        var data = Bound("exec-cup:2", UserId, "run1");

        // Act
        await bot.TapAsync(data);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), data));
            bot.Refusals.Last.Binding.Should().Be(new ConversationBinding(UserId, "run1"));
            bot.Refusals.Last.Conversation.Should().BeNull();
            bot.Journal.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapFromAnEarlierRun_ShouldRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run2");
        var data = Bound("exec-cup:2", UserId, "run1");

        // Act
        await bot.TapAsync(data);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), data));
            bot.Refusals.Last.Conversation!.Id.Should().Be("run2");
            bot.Journal.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapByAnotherUser_ShouldRefuseItAsNotYoursWithoutReadingTheStore()
    {
        // Arrange
        var store = new Mock<IConversationStore>();
        var bot = new Bot(store.Object);
        var data = Bound("exec-cup:2", OtherUserId, "run1");

        // Act
        await bot.TapAsync(data);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NotYours, typeof(Cup), data));
            bot.Refusals.Last.Conversation.Should().BeNull();
            store.Verify(x => x.GetAsync(It.IsAny<ConversationKey>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapByAnotherUserAtTheSameStep_ShouldNotTouchTheirConversation()
    {
        // Arrange: Anna is at the size step of her own order, as Nick is of his.
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "nick1");
        await bot.OpenAsync(Size, id: "anna1", userId: OtherUserId);

        // Act: Anna taps Nick's button.
        await bot.TapAsync(Bound("exec-cup:2", UserId, "nick1"), userId: OtherUserId);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Select(x => x.Reason).Should().Equal(ButtonRefusalReason.NotYours);
            bot.Journal.Entries.Should().BeEmpty();
            (await bot.CurrentAsync(OtherUserId)).Should().Be(new ConversationState(Flow, Size) { Id = "anna1" });
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapThatNoStepOfTheRunTakes_ShouldRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run1");
        var data = Bound("exec-cup:3", UserId, "run1");

        // Act
        await bot.TapAsync(data);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), data));
            bot.Refusals.Last.Conversation.Should().Be(new ConversationState(Flow, Size) { Id = "run1" });
            bot.Journal.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapThatARegularCommandTakes_ShouldRunIt()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run1");

        // Act
        await bot.TapAsync(Bound("exec-cup:1", UserId, "run1"));

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("cup");
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapOnAnInaccessibleMessage_ShouldRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run1");

        // Act
        await bot.TapAsync(Bound("exec-cup:2", UserId, "run1"), inaccessible: true);

        // Assert
        using (new AssertionScope())
        {
            bot.Refusals.Entries.Select(x => x.Reason).Should().Equal(ButtonRefusalReason.NoLongerActive);
            bot.Journal.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnboundTypedDataForAStep_ShouldNotRunTheStepAndShouldWarn()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size);

        // Act
        await bot.TapAsync("exec-cup:2");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().BeEmpty();
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.Unclaimed, typeof(Cup), "exec-cup:2"));
            bot.Logs.Entries.Should()
                .Equal(
                    (
                        LogLevel.Warning,
                        "Cup buttons are handled by the conversation step CupStep, so they must be bound to the "
                            + "conversation: build them with AddButton(text, button, await "
                            + "conversation.BindAsync(token))."
                    ),
                    (LogLevel.Debug, "Answered a tap on a Cup button as Unclaimed")
                );
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnclaimedTypedTapWithACatchAllCommand_ShouldStillRefuseItAsUnclaimed()
    {
        // Arrange
        var bot = new Bot(withCatchAll: true);

        // Act
        await bot.TapAsync("exec-cup:3");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("any");
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.Unclaimed, typeof(Cup), "exec-cup:3"));
        }
    }

    [Fact]
    public async Task ExecuteAsync_StaleTypedDataWithACatchAllCommand_ShouldStillRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot(withCatchAll: true);

        // Act
        await bot.TapAsync("exec-cup:large");

        // Assert
        bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), "exec-cup:large"));
    }

    [Fact]
    public async Task ExecuteAsync_BoundTapNoStepTakesWithACatchAllCommand_ShouldStillRefuseItAsNoLongerActive()
    {
        // Arrange
        var bot = new Bot(withCatchAll: true);
        await bot.OpenAsync(Size, id: "run1");
        var data = Bound("exec-cup:3", UserId, "run1");

        // Act
        await bot.TapAsync(data);

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("any");
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Cup), data));
        }
    }

    [Fact]
    public async Task ExecuteAsync_TypedTapAHandWrittenCallbackCommandTakes_ShouldNotRefuseIt()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync("exec-cup:4");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("hand-written cup");
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenRefusingATap_ShouldLogTheButtonAndTheReason()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync("exec-cup:large");

        // Assert
        bot.Logs.Entries.Should().Equal((LogLevel.Debug, "Answered a tap on a Cup button as NoLongerActive"));
    }

    [Fact]
    public async Task ExecuteAsync_HandWrittenDataWithAnAt_ShouldNotCountAsBound()
    {
        // Arrange: bound to someone else, it would be refused if it counted.
        var bot = new Bot();

        // Act
        await bot.TapAsync($"mention@{OtherUserId}.abc12345");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("mention");
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExecuteAsync_TwoBoundTapsAtOnce_ShouldHandleThemOneAfterTheOther()
    {
        // Arrange: the step that ends the conversation waits at a gate until the second tap waits for the lock.
        var bot = new Bot();
        await bot.OpenAsync(Size, id: "run1");
        bot.Gates.Close();
        var data = Bound("exec-pour:1", UserId, "run1");

        // Act
        var first = bot.TapAsync(data);
        await bot.Gates.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = bot.TapAsync(data);
        await bot.WaitForLockUsersAsync(2);
        bot.Gates.Open();
        await Task.WhenAll(first, second);

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("pour");
            bot.Refusals.Entries.Should().Equal((ButtonRefusalReason.NoLongerActive, typeof(Pour), data));
            (await bot.CurrentAsync()).Should().BeNull();
        }
    }

    [Theory]
    [InlineData("exec-cups:3")]
    [InlineData("cup:3")]
    [InlineData("forward")]
    public async Task ExecuteAsync_ForHandWrittenButtonData_ShouldNeverRefuse(string data)
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.TapAsync(data);

        // Assert
        bot.Refusals.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ForAMessage_ShouldNeverRefuse()
    {
        // Arrange
        var bot = new Bot();

        // Act
        await bot.SendAsync("/unknown");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().BeEmpty();
            bot.Refusals.Entries.Should().BeEmpty();
        }
    }

    private static string Bound(string data, long userId, string conversationId) => $"{data}@{userId}.{conversationId}";

    private static string? TextOf(CommandRequest request) => request.Update.Message?.Text;

    private static bool IsPlainText(CommandRequest request) => TextOf(request) is { } text && !text.StartsWith('/');

    private sealed class Bot
    {
        private static readonly Type[] Commands =
        [
            typeof(StartCommand),
            typeof(EchoCommand),
            typeof(CancelCommand),
            typeof(PickSize),
            typeof(WriteName),
            typeof(Back),
            typeof(CupCommand),
            typeof(CupStep),
            typeof(PourStep),
            typeof(Mention),
            typeof(HandWrittenCupCommand),
        ];

        private static readonly IOptionsMonitor<ParallelCommandExecutionConfiguration> Options = Mock.Of<
            IOptionsMonitor<ParallelCommandExecutionConfiguration>
        >(x => x.CurrentValue == new ParallelCommandExecutionConfiguration());

        private readonly CommandCatalog _catalog;
        private readonly ButtonCatalog _buttons;
        private readonly ServiceProvider _provider;

        // withCatchAll adds a raw command that takes every update.
        public Bot(IConversationStore? store = null, bool withSteps = true, bool withCatchAll = false)
        {
            Store = store ?? new InMemoryConversationStore();
            Type[] types = withCatchAll ? [.. Commands, typeof(CatchAll)] : Commands;

            var commands = types
                .Select(type => new CatalogedCommand(
                    type,
                    CommandPriority.Default,
                    type.GetCustomAttribute<ConversationStepAttribute>()
                ))
                .Where(x => withSteps || x.Step is null)
                .ToArray();

            _catalog = new CommandCatalog(commands);
            _buttons = ButtonCatalog.Scan([typeof(Cup), typeof(Pour)], commands);

            var services = new ServiceCollection()
                .AddSingleton(Store)
                .AddSingleton(Journal)
                .AddSingleton(Gates)
                .AddSingleton<IButtonRefusalHandler>(Refusals)
                .AddScoped<Conversation>()
                .AddScoped<IConversation>(provider => provider.GetRequiredService<Conversation>());

            foreach (var command in types)
            {
                services.AddScoped(command);
            }

            _provider = services.BuildServiceProvider();
        }

        public IConversationStore Store { get; }

        public Journal Journal { get; } = new();

        public RecordingRefusals Refusals { get; } = new();

        public RecordingLogger Logs { get; } = new();

        public Gates Gates { get; } = new();

        public ConversationLocks Locks { get; } = new();

        public Task OpenAsync(string step, string? id = null, long userId = UserId) =>
            Store.SaveAsync(
                new ConversationKey(ChatId, userId),
                new ConversationState(Flow, step) { Id = id },
                CancellationToken.None
            );

        public Task<ConversationState?> CurrentAsync(long userId = UserId) =>
            Store.GetAsync(new ConversationKey(ChatId, userId), CancellationToken.None);

        // Yields until that many taps hold or await the sender's lock.
        public async Task WaitForLockUsersAsync(int users)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Locks.UsersOf(Sender) < users)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        public Task SendAsync(string text, long userId = UserId) =>
            HandleAsync(
                new Update
                {
                    Message = new Message
                    {
                        Text = text,
                        Chat = new Chat { Id = ChatId },
                        From = new User { Id = userId },
                    },
                }
            );

        // An inaccessible message is one Telegram no longer shows the bot, dated 0; read as Telegram.Bot reads it.
        public Task TapAsync(string data, long userId = UserId, bool inaccessible = false) =>
            HandleAsync(
                new Update
                {
                    CallbackQuery = new CallbackQuery
                    {
                        Id = "query",
                        Data = data,
                        From = new User { Id = userId },
                        Message = inaccessible
                            ? JsonSerializer.Deserialize<Message>(
                                $$$"""{"message_id":1,"date":0,"chat":{"id":{{{ChatId}}},"type":"private"}}""",
                                JsonBotAPI.Options
                            )
                            : new Message
                            {
                                Chat = new Chat { Id = ChatId },
                                Date = DateTime.UtcNow,
                            },
                    },
                }
            );

        private async Task HandleAsync(Update update)
        {
            await using var scope = _provider.CreateAsyncScope();
            var services = scope.ServiceProvider;

            var conversation = services.GetRequiredService<Conversation>();
            conversation.Bind(update);

            var commands = new ParallelTelegramCommandExecutor(
                new CommandPriorityAccessor([.. _catalog.Regular.Select(x => x.Resolve(services))]),
                Options
            );
            var sut = new ConversationAwareCommandExecutor(
                conversation,
                _catalog,
                commands,
                _buttons,
                Locks,
                Logs,
                services
            );

            await sut.ExecuteAsync(new CommandRequest(update, Mock.Of<ITelegramBotClient>()), CancellationToken.None);
        }
    }

    private sealed class Journal
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public void Write(string entry)
        {
            lock (_entries)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class RecordingRefusals : IButtonRefusalHandler
    {
        private readonly List<ButtonRefusal> _refusals = [];

        public IReadOnlyList<(ButtonRefusalReason Reason, Type ButtonType, string? Data)> Entries
        {
            get
            {
                lock (_refusals)
                {
                    return [.. _refusals.Select(x => (x.Reason, x.ButtonType, x.Query.Data))];
                }
            }
        }

        public ButtonRefusal Last
        {
            get
            {
                lock (_refusals)
                {
                    return _refusals[^1];
                }
            }
        }

        public Task HandleAsync(ButtonRefusal refusal, CancellationToken token)
        {
            lock (_refusals)
            {
                _refusals.Add(refusal);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<ConversationAwareCommandExecutor>
    {
        private readonly List<(LogLevel, string)> _entries = [];

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
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    // Lets a test hold a step until it chooses; open by default.
    private sealed class Gates
    {
        private TaskCompletionSource _release = CompletedGate();

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Release => _release.Task;

        public void Close() => _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _release.TrySetResult();

        private static TaskCompletionSource CompletedGate()
        {
            var gate = new TaskCompletionSource();
            gate.SetResult();
            return gate;
        }
    }

    [ButtonData("exec-pour")]
    private readonly record struct Pour(int Cups);

    // Ends the conversation, once the gate lets it.
    [ConversationStep(Flow, Size)]
    private sealed class PourStep(Journal journal, Gates gates, IConversation conversation) : CallbackQueryCommand<Pour>
    {
        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            gates.Entered.TrySetResult();
            await gates.Release;

            journal.Write("pour");
            await conversation.EndAsync(token);
        }
    }

    // Hand-written data that happens to hold an '@'.
    private sealed class Mention(Journal journal) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(request.Update.CallbackQuery?.Data?.StartsWith("mention@") is true);

        public Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("mention");
            return Task.CompletedTask;
        }
    }

    // Takes every update, as an audit command might.
    private sealed class CatchAll(Journal journal) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) => Task.FromResult(true);

        public Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("any");
            return Task.CompletedTask;
        }
    }

    // A callback command without typed data that takes one cup button.
    private sealed class HandWrittenCupCommand(Journal journal) : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data == "exec-cup:4");

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            journal.Write("hand-written cup");
            return Task.CompletedTask;
        }
    }

    [ButtonData("exec-cup")]
    private readonly record struct Cup(int Size);

    // Takes cups of size 1; any other is left for the step, or for nobody.
    private sealed class CupCommand(Journal journal) : CallbackQueryCommand<Cup>
    {
        protected override Task<ButtonCheck> CheckAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(Parsed.Size == 1 ? ButtonCheck.Accept : ButtonCheck.Decline);

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            journal.Write("cup");
            return Task.CompletedTask;
        }
    }

    // At the size step, takes cups of size 2.
    [ConversationStep(Flow, Size)]
    private sealed class CupStep(Journal journal) : CallbackQueryCommand<Cup>
    {
        protected override Task<ButtonCheck> CheckAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(Parsed.Size == 2 ? ButtonCheck.Accept : ButtonCheck.Decline);

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            journal.Write("cup at the size step");
            return Task.CompletedTask;
        }
    }

    private sealed class StartCommand(Journal journal, IConversation conversation) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(TextOf(request) == "/order");

        public async Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("start");
            await conversation.StartAsync(Flow, Size, token);
        }
    }

    private sealed class EchoCommand(Journal journal) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(IsPlainText(request));

        public Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("echo");
            return Task.CompletedTask;
        }
    }

    private sealed class CancelCommand(Journal journal, IConversation conversation) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(TextOf(request) == "/cancel");

        public async Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("cancel");
            await conversation.EndAsync(token);
        }
    }

    [ConversationStep(Flow, Size)]
    private sealed class PickSize(Journal journal, IConversation conversation) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(IsPlainText(request));

        public async Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("size");
            await conversation.MoveToAsync(Name, token);
        }
    }

    [ConversationStep(Flow, Name)]
    private sealed class WriteName(Journal journal, IConversation conversation) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(IsPlainText(request));

        public async Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("name");
            await conversation.EndAsync(token);
        }
    }

    [ConversationStep(Flow)]
    private sealed class Back(Journal journal) : ITelegramCommand
    {
        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
            Task.FromResult(request.Update.CallbackQuery?.Data == "back");

        public Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            journal.Write("back");
            return Task.CompletedTask;
        }
    }
}
