using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// The points card's "redeem:{ownerId}:{points}" buttons. The data is parsed before the member is looked up, and a tap
// from someone no longer a member goes unanswered.
internal sealed class RedeemButton(MemberDirectory members, PointsCard card)
    : KnownUserCallbackQueryCommand<Member, (long OwnerId, int Points)>
{
    private const string Prefix = "redeem";

    public static string Data(long ownerId, int points) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{ownerId}:{points}");

    protected override (long OwnerId, int Points)? Parse(string data) =>
        data.Split(':') is [Prefix, var owner, var amount]
        && long.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId)
        && int.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out var points)
        && points > 0
            ? (ownerId, points)
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var (ownerId, points) = Parsed;

        if (ownerId != User.UserId)
        {
            var owner = members.Find(ownerId)?.Name ?? "someone else";
            await AlertAsync(client, query, $"This card is {owner}'s — send /points for your own.", token);
            return;
        }

        if (members.Spend(User.UserId, points) is not { } after)
        {
            await AlertAsync(client, query, $"You have only {User.Points} points.", token);
            return;
        }

        await client.AnswerCallbackQuery(query.Id, $"Redeemed {points} points", cancellationToken: token);
        await card.ShowAsync(client, query.Message!.Chat, after, token);
    }

    private static Task AlertAsync(
        ITelegramBotClient client,
        CallbackQuery query,
        string text,
        CancellationToken token
    ) => client.AnswerCallbackQuery(query.Id, text, showAlert: true, cancellationToken: token);
}
