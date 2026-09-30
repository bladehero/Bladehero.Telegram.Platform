using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

[BotCommand("leave", "Leave the loyalty club")]
internal sealed class LeaveCommand(MemberDirectory members) : KnownUserMessageCommand<Member>
{
    protected override bool Matches(Message message) => message.IsCommand("/leave");

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        members.Leave(User.UserId);
        return request.Client.SendMessage(request.Payload.Chat, "You left the club.", cancellationToken: token);
    }
}
