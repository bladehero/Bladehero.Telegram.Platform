using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// A points card's button, "redeem:{ownerId}:{points}".
[ButtonData("redeem")]
internal readonly record struct Redeem(long OwnerId, int Points);

// A tap from someone no longer a member is answered silently, as no command takes it.
internal sealed class RedeemButton(MemberDirectory members, PointsCard card)
    : KnownUserCallbackQueryCommand<Member, Redeem>
{
    protected override Task<ButtonCheck> CheckAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        if (Parsed.Points <= 0)
        {
            return Task.FromResult(ButtonCheck.Decline);
        }

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
        var (_, points) = Parsed;

        if (members.Spend(User.UserId, points) is not { } after)
        {
            await client.AnswerCallbackQuery(
                query.Id,
                $"You have only {User.Points} points.",
                showAlert: true,
                cancellationToken: token
            );
            return;
        }

        await client.AnswerCallbackQuery(query.Id, $"Redeemed {points} points", cancellationToken: token);
        await card.UpdateAsync(TelegramMessageRef.From(query), after, token);
    }
}
