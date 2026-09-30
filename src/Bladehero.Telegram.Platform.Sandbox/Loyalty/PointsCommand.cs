using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// Strangers are declined before this runs, so a /points from them goes unanswered.
[BotCommand("points", "Your loyalty points")]
internal sealed class PointsCommand(PointsCard card) : KnownUserMessageCommand<Member>
{
    protected override bool Matches(Message message) => message.IsCommand("/points");

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        card.SendAsync(request.Client, request.Payload.Chat, User, token);
}
