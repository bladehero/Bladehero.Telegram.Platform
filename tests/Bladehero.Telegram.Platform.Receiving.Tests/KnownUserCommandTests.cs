using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

public sealed class KnownUserCommandTests
{
    private const long KnownChat = 42;

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
        Assert.Equal(0, resolver.Calls);
    }

    private static CommandRequest RequestFrom(long chatId, string text) =>
        new(
            new Update
            {
                Id = 1,
                Message = new Message
                {
                    Text = text,
                    Chat = new Chat { Id = chatId },
                },
            },
            Client
        );

    private sealed record TestUser(string Name);

    private sealed class Resolver : ITelegramUserResolver<TestUser>
    {
        public int Calls { get; private set; }

        public Task<TestUser?> ResolveAsync(long chatId, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(chatId == KnownChat ? new TestUser("Nick") : null);
        }
    }

    private sealed class ProbeCommand(ITelegramUserResolver<TestUser> users) : KnownUserCommand<TestUser>(users)
    {
        public TestUser? ResolvedUser { get; private set; }

        protected override bool Matches(Message message) => message.IsCommand("/probe");

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            ResolvedUser = User;
            return Task.CompletedTask;
        }
    }
}
