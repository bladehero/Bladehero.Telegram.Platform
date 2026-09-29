using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform;

internal sealed class TelegramSender(ITelegramBotClient client) : ITelegramSender
{
    public Task<Message> SendAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = ParseMode.None,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default
    ) => client.SendMessage(chatId, text, parseMode, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
}
