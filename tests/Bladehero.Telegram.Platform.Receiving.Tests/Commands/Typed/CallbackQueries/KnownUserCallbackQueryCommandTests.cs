using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Typed.CallbackQueries;

public sealed class KnownUserCallbackQueryCommandTests
{
    private const long KnownChat = 42;

    private static readonly Guid ExpenseId = Guid.NewGuid();

    private static readonly ITelegramBotClient Client = Mock.Of<ITelegramBotClient>();

    [Fact]
    public async Task CanHandleAsync_WhenTheChatIsKnown_ShouldHandleWithTheUserAndTheParsedValue()
    {
        // Arrange
        var sut = new DeleteCommand(new Resolver());
        var request = Tap(KnownChat, $"delete:{ExpenseId:N}");

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeTrue();
            sut.Handled.Should().Be(("Nick", "delete", ExpenseId));
        }
    }

    [Fact]
    public async Task CanHandleAsync_ShouldResolveWhoTappedInTheChatOfTheButton()
    {
        // Arrange
        var resolver = new Resolver();
        var sut = new DeleteCommand(resolver);

        // Act
        await sut.CanHandleAsync(Tap(KnownChat, $"delete:{ExpenseId:N}"), CancellationToken.None);

        // Assert
        resolver.Seen.Should().Equal((KnownChat, 7L));
    }

    [Fact]
    public async Task CanHandleAsync_WhenTheChatIsUnknown_ShouldDecline()
    {
        // Arrange
        var sut = new DeleteCommand(new Resolver());

        // Act
        var canHandle = await sut.CanHandleAsync(Tap(999, $"delete:{ExpenseId:N}"), CancellationToken.None);

        // Assert
        canHandle.Should().BeFalse();
    }

    [Fact]
    public async Task CanHandleAsync_WhenTheDataDoesNotParse_ShouldDeclineWithoutResolvingAUser()
    {
        // Arrange
        var resolver = new Resolver();
        var sut = new DeleteCommand(resolver);

        // Act
        var canHandle = await sut.CanHandleAsync(Tap(KnownChat, "page:2"), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeFalse();
            resolver.Calls.Should().Be(0);
        }
    }

    [Fact]
    public async Task CanHandleAsync_WhenTheButtonIsOnAnInlineModeMessage_ShouldDeclineWithoutResolvingAUser()
    {
        // Arrange
        var resolver = new Resolver();
        var sut = new DeleteCommand(resolver);
        var request = new CommandRequest(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "query",
                    Data = $"delete:{ExpenseId:N}",
                    From = new User { Id = 7 },
                    InlineMessageId = "inline",
                },
            },
            Client
        );

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeFalse();
            resolver.Calls.Should().Be(0);
        }
    }

    private static CommandRequest Tap(long chatId, string data) =>
        new(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "query",
                    Data = data,
                    From = new User { Id = 7 },
                    Message = new Message { Chat = new Chat { Id = chatId } },
                },
            },
            Client
        );

    private sealed record TestUser(string Name);

    private sealed class Resolver : ITelegramUserResolver<TestUser>
    {
        public List<(long ChatId, long UserId)> Seen { get; } = [];

        public int Calls => Seen.Count;

        public Task<TestUser?> ResolveAsync(long chatId, long userId, CancellationToken token)
        {
            Seen.Add((chatId, userId));
            return Task.FromResult(chatId == KnownChat ? new TestUser("Nick") : null);
        }
    }

    private sealed class DeleteCommand : KnownUserCallbackQueryCommand<TestUser, (string Action, Guid Id)>
    {
        public DeleteCommand(ITelegramUserResolver<TestUser> users)
        {
            UserResolver = users;
        }

        public (string User, string Action, Guid Id)? Handled { get; private set; }

        protected override (string Action, Guid Id)? Parse(string data) =>
            data.Split(':') is ["delete", var id] && Guid.TryParse(id, out var parsed) ? ("delete", parsed) : null;

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            Handled = (User.Name, Parsed.Action, Parsed.Id);
            return Task.CompletedTask;
        }
    }
}
