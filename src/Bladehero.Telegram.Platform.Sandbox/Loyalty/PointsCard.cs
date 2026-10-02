using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// Each member's points card, found again from the chat's history, so nothing is kept in memory.
internal sealed class PointsCard(ITelegramMessages messages, ITelegramHistory history)
{
    // /points: a fresh card at the bottom, replacing the member's last one in this chat.
    public async Task SendAsync(Chat chat, Member member, CancellationToken token)
    {
        var previous = await history.FindLatestWithButtonAsync<Redeem>(chat.Id, x => x.OwnerId == member.UserId, token);

        if (previous is { } card)
        {
            await messages.ReplaceAsync(card, TextOf(member), Buttons(member.UserId), token: token);
        }
        else
        {
            await messages.SendAsync(chat.Id, TextOf(member), Buttons(member.UserId), token: token);
        }
    }

    // A tap: the card shows the new points.
    public Task UpdateAsync(TelegramMessageRef card, Member member, CancellationToken token) =>
        messages.ShowAsync(card, TextOf(member), Buttons(member.UserId), token: token);

    // Deletes the card, or takes its buttons off when it can't be deleted.
    public Task CloseAsync(TelegramMessageRef card, CancellationToken token) => messages.DeleteAsync(card, token);

    private static string TextOf(Member member) => $"{member.Name}, you have {member.Points} points.";

    private static InlineKeyboardMarkup Buttons(long ownerId) =>
        new InlineKeyboardMarkup()
            .AddButton("Redeem 10", new Redeem(ownerId, 10))
            .AddButton("Redeem 50", new Redeem(ownerId, 50))
            .AddNewRow()
            .AddButton("✖ Close", new ClosePoints(ownerId));
}
