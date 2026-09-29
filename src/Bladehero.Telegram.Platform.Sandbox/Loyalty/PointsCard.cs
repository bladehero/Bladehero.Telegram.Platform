using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// Each member's points card, one message per chat: edited in place, and sent again only when it cannot be.
internal sealed class PointsCard
{
    private static InlineKeyboardMarkup Buttons =>
        new([
            [
                InlineKeyboardButton.WithCallbackData("Redeem 10", RedeemButton.Data(10)),
                InlineKeyboardButton.WithCallbackData("Redeem 50", RedeemButton.Data(50)),
            ],
        ]);

    private readonly ConcurrentDictionary<(long ChatId, long UserId), int> _cards = new();

    public async Task ShowAsync(ITelegramBotClient client, Chat chat, Member member, CancellationToken token)
    {
        var text = $"{member.Name}, you have {member.Points} points.";
        var key = (chat.Id, member.UserId);

        if (_cards.TryGetValue(key, out var cardId))
        {
            try
            {
                await client.EditMessageText(chat, cardId, text, replyMarkup: Buttons, cancellationToken: token);
                return;
            }
            catch (ApiRequestException error) when (error.Message.Contains("message is not modified"))
            {
                return;
            }
            catch (ApiRequestException error) when (error.Message.Contains("message to edit not found"))
            {
                // Gone, so a new card takes its place.
            }
            catch (ApiRequestException)
            {
                await DeleteAsync(client, chat, cardId, token);
            }
        }

        var card = await client.SendMessage(chat, text, replyMarkup: Buttons, cancellationToken: token);
        _cards[key] = card.Id;
    }

    // A card that cannot be edited is replaced; one that cannot be deleted either stays behind.
    private static async Task DeleteAsync(ITelegramBotClient client, Chat chat, int cardId, CancellationToken token)
    {
        try
        {
            await client.DeleteMessage(chat, cardId, token);
        }
        catch (ApiRequestException)
        {
            // Better an old card left behind than no card at all.
        }
    }
}
