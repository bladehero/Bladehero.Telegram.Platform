using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// The points card's "redeem:{points}" buttons. The amount is parsed before the member is looked up, and a tap from
// someone no longer a member goes unanswered.
internal sealed class RedeemButton(MemberDirectory members, PointsCard card)
    : KnownUserCallbackQueryCommand<Member, int>
{
    private const string Prefix = "redeem:";

    public static string Data(int points) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{points}");

    protected override int? Parse(string data) =>
        data.StartsWith(Prefix, StringComparison.Ordinal)
        && int.TryParse(data[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var points)
        && points > 0
            ? points
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;

        if (members.Spend(User.UserId, Parsed) is not { } after)
        {
            await client.AnswerCallbackQuery(
                query.Id,
                $"You have only {User.Points} points.",
                showAlert: true,
                cancellationToken: token
            );
            return;
        }

        await client.AnswerCallbackQuery(query.Id, $"Redeemed {Parsed} points", cancellationToken: token);
        await card.ShowAsync(client, query.Message!.Chat, after, token);
    }
}
