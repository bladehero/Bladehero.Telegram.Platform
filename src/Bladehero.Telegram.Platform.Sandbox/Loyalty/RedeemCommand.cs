using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// "/redeem 10", the amount after the command, on the same line or the next.
[BotCommand("redeem", "Redeem points, e.g. /redeem 10")]
internal sealed class RedeemCommand(MemberDirectory members) : KnownUserMessageCommand<Member>
{
    protected override bool Matches(Message message) => message.IsCommand("/redeem");

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        return client.SendMessage(message.Chat, Redeem(message.ArgumentsOf("/redeem")), cancellationToken: token);
    }

    private string Redeem(string? amount)
    {
        if (amount is null)
        {
            return "How many? Try /redeem 10";
        }

        if (!int.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out var points) || points == 0)
        {
            return "That's not a number of points.";
        }

        return members.Spend(User.UserId, points) is { } after
            ? $"Redeemed {points} points — enjoy a free cookie! {after.Points} left."
            : $"You have only {User.Points} points.";
    }
}
