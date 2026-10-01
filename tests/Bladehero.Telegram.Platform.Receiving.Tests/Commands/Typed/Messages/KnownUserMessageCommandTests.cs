using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Typed.Messages;

public sealed class KnownUserMessageCommandTests
{
    private const long KnownChat = 42;
    private const long Family = -1001;

    private static readonly ITelegramBotClient Client = new TelegramBotClient(
        "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"
    );

    [Fact]
    public async Task AKnownChatRunsTheCommandAndSeesItsUser()
    {
        var command = new ProbeCommand(new Resolver());
        var request = RequestFrom(KnownChat, "/probe");

        var canHandle = await command.CanHandleAsync(request, CancellationToken.None);
        await command.HandleAsync(request, CancellationToken.None);

        Assert.True(canHandle);
        Assert.Equal("Nick", command.ResolvedUser?.Name);
    }

    [Fact]
    public async Task AnUnknownChatIsDeclinedRatherThanThrowing()
    {
        var command = new ProbeCommand(new Resolver());

        var canHandle = await command.CanHandleAsync(RequestFrom(999, "/probe"), CancellationToken.None);

        Assert.False(canHandle);
    }

    [Fact]
    public async Task AMessageThatDoesNotMatchIsDeclinedWithoutResolvingAUser()
    {
        var resolver = new Resolver();
        var command = new ProbeCommand(resolver);

        var canHandle = await command.CanHandleAsync(RequestFrom(KnownChat, "hello"), CancellationToken.None);

        Assert.False(canHandle);
        Assert.Empty(resolver.Seen);
    }

    [Fact]
    public async Task AGroupMembersMessageResolvesThatMemberInThatGroup()
    {
        var resolver = new Resolver();
        var command = new ProbeCommand(resolver);

        var canHandle = await command.CanHandleAsync(
            RequestFrom(Family, "/probe", sender: KnownChat),
            CancellationToken.None
        );

        Assert.True(canHandle);
        Assert.Equal([(Family, KnownChat)], resolver.Seen);
    }

    [Fact]
    public async Task AMessageWithoutASenderIsDeclinedWithoutResolving()
    {
        var resolver = new Resolver();
        var command = new ProbeCommand(resolver);
        var request = new CommandRequest(
            new Update
            {
                Id = 1,
                Message = new Message
                {
                    Text = "/probe",
                    Chat = new Chat { Id = Family },
                },
            },
            Client
        );

        var canHandle = await command.CanHandleAsync(request, CancellationToken.None);

        Assert.False(canHandle);
        Assert.Empty(resolver.Seen);
    }

    [Theory]
    [InlineData(136817688)] // @Channel_Bot, for a channel's post in its discussion group
    [InlineData(1087968824)] // @GroupAnonymousBot, for an anonymous admin
    [InlineData(777000)] // Telegram, for a post forwarded automatically from a linked channel
    public async Task AMessageSentOnBehalfOfAChatIsDeclinedWithoutResolving(long placeholder)
    {
        var resolver = new Resolver();
        var command = new ProbeCommand(resolver);
        var request = new CommandRequest(
            new Update
            {
                Id = 1,
                Message = new Message
                {
                    Text = "/probe",
                    Chat = new Chat { Id = Family },
                    From = new User { Id = placeholder },
                    SenderChat = new Chat { Id = -1001234567890 },
                },
            },
            Client
        );

        var canHandle = await command.CanHandleAsync(request, CancellationToken.None);

        Assert.False(canHandle);
        Assert.Empty(resolver.Seen);
    }

    [Fact]
    public async Task CanHandleAsync_WhenAcceptsDeclines_ShouldDecline()
    {
        // Arrange
        var sut = new ProbeCommand(new Resolver()) { Accepts = false };

        // Act
        var canHandle = await sut.CanHandleAsync(RequestFrom(KnownChat, "/probe"), CancellationToken.None);

        // Assert
        canHandle.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptsAsync_ShouldSeeTheResolvedUser()
    {
        // Arrange
        var sut = new ProbeCommand(new Resolver());

        // Act
        await sut.CanHandleAsync(RequestFrom(KnownChat, "/probe"), CancellationToken.None);

        // Assert
        sut.AskedFor.Should().Equal("Nick");
    }

    [Fact]
    public async Task CanHandleAsync_WhenTheUserIsUnknown_ShouldNotAskAccepts()
    {
        // Arrange
        var sut = new ProbeCommand(new Resolver());

        // Act
        var canHandle = await sut.CanHandleAsync(RequestFrom(999, "/probe"), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeFalse();
            sut.AskedFor.Should().BeEmpty();
        }
    }

    // From the chat's own user, as in a private chat, unless a sender is given.
    private static CommandRequest RequestFrom(long chatId, string text, long? sender = null) =>
        new(
            new Update
            {
                Id = 1,
                Message = new Message
                {
                    Text = text,
                    Chat = new Chat { Id = chatId },
                    From = new User { Id = sender ?? chatId },
                },
            },
            Client
        );

    private sealed record TestUser(string Name);

    // Knows the user of KnownChat, in every chat.
    private sealed class Resolver : ITelegramUserResolver<TestUser>
    {
        public List<(long ChatId, long UserId)> Seen { get; } = [];

        public Task<TestUser?> ResolveAsync(long chatId, long userId, CancellationToken token)
        {
            Seen.Add((chatId, userId));
            return Task.FromResult(userId == KnownChat ? new TestUser("Nick") : null);
        }
    }

    private sealed class ProbeCommand : KnownUserMessageCommand<TestUser>
    {
        public ProbeCommand(ITelegramUserResolver<TestUser> users)
        {
            UserResolver = users;
        }

        public TestUser? ResolvedUser { get; private set; }

        public bool Accepts { get; init; } = true;

        public List<string> AskedFor { get; } = [];

        protected override bool Matches(Message message) => message.IsCommand("/probe");

        protected override Task<bool> AcceptsAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            AskedFor.Add(User.Name);
            return Task.FromResult(Accepts);
        }

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            ResolvedUser = User;
            return Task.CompletedTask;
        }
    }
}
