using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// Open to anyone, so a regular command rather than a known-user one.
[BotCommand("join", "Join the loyalty club")]
internal sealed class JoinCommand(MemberDirectory members) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.From is not null && request.Payload.IsCommand("/join"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var sender = message.From!;

        return client.SendMessage(
            message.Chat,
            members.Join(sender.Id, sender.FirstName)
                ? $"Welcome to the club, {sender.FirstName}!"
                : "You're already a member.",
            cancellationToken: token
        );
    }
}
