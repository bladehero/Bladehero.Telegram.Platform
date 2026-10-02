using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// A points card's Close button, "points-close:{ownerId}".
[ButtonData("points-close")]
internal readonly record struct ClosePoints(long OwnerId);

internal sealed class CloseButton(MemberDirectory members, PointsCard card)
    : KnownUserCallbackQueryCommand<Member, ClosePoints>
{
    protected override Task<ButtonCheck> CheckAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        if (Parsed.OwnerId != User.UserId)
        {
            var owner = members.Find(Parsed.OwnerId)?.Name ?? "someone else";
            return Task.FromResult(
                ButtonCheck.Reject($"This card is {owner}'s — send /points for your own.", showAlert: true)
            );
        }

        return Task.FromResult(ButtonCheck.Accept);
    }

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await card.CloseAsync(TelegramMessageRef.From(query), token);
    }
}
