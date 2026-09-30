using System.Reflection;
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
    public async Task ExecuteAsync_WhenAStepTakesATypedButton_ShouldNotRefuseIt()
    {
        // Arrange
        var bot = new Bot();
        await bot.OpenAsync(Size);

        // Act
        await bot.TapAsync("exec-cup:2");

        // Assert
        using (new AssertionScope())
        {
            bot.Journal.Entries.Should().Equal("cup at the size step");
            bot.Refusals.Entries.Should().BeEmpty();
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
        ];

        private static readonly IOptionsMonitor<ParallelCommandExecutionConfiguration> Options = Mock.Of<
            IOptionsMonitor<ParallelCommandExecutionConfiguration>
        >(x => x.CurrentValue == new ParallelCommandExecutionConfiguration());

        private readonly CommandCatalog _catalog;
        private readonly ButtonCatalog _buttons;
        private readonly ServiceProvider _provider;

        public Bot(IConversationStore? store = null, bool withSteps = true)
        {
            Store = store ?? new InMemoryConversationStore();

            var commands = Commands
                .Select(type => new CatalogedCommand(
                    type,
                    CommandPriority.Default,
                    type.GetCustomAttribute<ConversationStepAttribute>()
                ))
                .Where(x => withSteps || x.Step is null)
                .ToArray();

            _catalog = new CommandCatalog(commands);
            _buttons = ButtonCatalog.Scan([typeof(Cup)], commands);

            var services = new ServiceCollection()
                .AddSingleton(Store)
                .AddSingleton(Journal)
                .AddSingleton<IButtonRefusalHandler>(Refusals)
                .AddScoped<Conversation>()
                .AddScoped<IConversation>(provider => provider.GetRequiredService<Conversation>());

            foreach (var command in Commands)
            {
                services.AddScoped(command);
            }

            _provider = services.BuildServiceProvider();
        }

        public IConversationStore Store { get; }

        public Journal Journal { get; } = new();

        public RecordingRefusals Refusals { get; } = new();

        public Task OpenAsync(string step) =>
            Store.SaveAsync(Sender, new ConversationState(Flow, step), CancellationToken.None);

        public Task<ConversationState?> CurrentAsync() => Store.GetAsync(Sender, CancellationToken.None);

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

        public Task TapAsync(string data) =>
            HandleAsync(
                new Update
                {
                    CallbackQuery = new CallbackQuery
                    {
                        Id = "query",
                        Data = data,
                        From = new User { Id = UserId },
                        Message = new Message { Chat = new Chat { Id = ChatId } },
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
            var sut = new ConversationAwareCommandExecutor(conversation, _catalog, commands, _buttons, services);

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
        private readonly List<(ButtonRefusalReason, Type, string?)> _entries = [];

        public IReadOnlyList<(ButtonRefusalReason Reason, Type ButtonType, string? Data)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public Task HandleAsync(ButtonRefusal refusal, CancellationToken token)
        {
            lock (_entries)
            {
                _entries.Add((refusal.Reason, refusal.ButtonType, refusal.Query.Data));
            }

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
