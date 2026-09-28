using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
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
        ];

        private static readonly IOptionsMonitor<ParallelCommandExecutionConfiguration> Options = Mock.Of<
            IOptionsMonitor<ParallelCommandExecutionConfiguration>
        >(x => x.CurrentValue == new ParallelCommandExecutionConfiguration());

        private readonly CommandCatalog _catalog;
        private readonly ServiceProvider _provider;

        public Bot(IConversationStore? store = null, bool withSteps = true)
        {
            Store = store ?? new InMemoryConversationStore();

            _catalog = new CommandCatalog(
                Commands
                    .Select(type => new CatalogedCommand(
                        type,
                        CommandPriority.Default,
                        type.GetCustomAttribute<ConversationStepAttribute>()
                    ))
                    .Where(x => withSteps || x.Step is null)
            );

            var services = new ServiceCollection()
                .AddSingleton(Store)
                .AddSingleton(Journal)
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
            var sut = new ConversationAwareCommandExecutor(conversation, _catalog, commands, services);

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
