using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// Each member's points card, one per chat: /points sends a fresh one to the bottom, a tap updates it in place.
internal sealed class PointsCard
{
    private readonly ConcurrentDictionary<(long ChatId, long UserId), int> _cards = new();

    public async Task SendAsync(ITelegramBotClient client, Chat chat, Member member, CancellationToken token)
    {
        if (_cards.TryRemove((chat.Id, member.UserId), out var previous))
        {
            await DeleteAsync(client, chat, previous, token);
        }

        var card = await client.SendMessage(
            chat,
            TextOf(member),
            replyMarkup: Buttons(member.UserId),
            cancellationToken: token
        );
        _cards[(chat.Id, member.UserId)] = card.Id;
    }

    public async Task UpdateAsync(
        ITelegramBotClient client,
        Chat chat,
        int cardId,
        Member member,
        CancellationToken token
    )
    {
        try
        {
            await client.EditMessageText(
                chat,
                cardId,
                TextOf(member),
                replyMarkup: Buttons(member.UserId),
                cancellationToken: token
            );
        }
        catch (ApiRequestException error) when (error.Message.Contains("message is not modified"))
        {
            // It shows this already.
        }
        catch (ApiRequestException)
        {
            // Refused, so a fresh card replaces it.
            Forget(chat, member.UserId, cardId);
            await DeleteAsync(client, chat, cardId, token);
            await SendAsync(client, chat, member, token);
        }
    }

    // Deletes the card, or takes its buttons off when it can't be deleted.
    public async Task CloseAsync(
        ITelegramBotClient client,
        Chat chat,
        int cardId,
        long ownerId,
        CancellationToken token
    )
    {
        Forget(chat, ownerId, cardId);
        if (await DeleteAsync(client, chat, cardId, token))
        {
            return;
        }

        try
        {
            await client.EditMessageReplyMarkup(chat, cardId, replyMarkup: null, cancellationToken: token);
        }
        catch (ApiRequestException)
        {
            // Gone, or without buttons already.
        }
    }

    private static string TextOf(Member member) => $"{member.Name}, you have {member.Points} points.";

    private static InlineKeyboardMarkup Buttons(long ownerId) =>
        new InlineKeyboardMarkup()
            .AddButton("Redeem 10", new Redeem(ownerId, 10))
            .AddButton("Redeem 50", new Redeem(ownerId, 50))
            .AddNewRow()
            .AddButton("✖ Close", new ClosePoints(ownerId));

    // Only while it is still the member's tracked card, not an older one left behind.
    private void Forget(Chat chat, long userId, int cardId) => _cards.TryRemove(new((chat.Id, userId), cardId));

    // False when Telegram refuses: the card is gone already, or too old to delete.
    private static async Task<bool> DeleteAsync(
        ITelegramBotClient client,
        Chat chat,
        int cardId,
        CancellationToken token
    )
    {
        try
        {
            await client.DeleteMessage(chat, cardId, token);
            return true;
        }
        catch (ApiRequestException)
        {
            return false;
        }
    }
}
