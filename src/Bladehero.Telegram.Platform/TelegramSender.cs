using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform;

internal sealed class TelegramSender(TelegramBotClientAccessor accessor) : ITelegramSender
{
    public Task<Message> SendAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = ParseMode.None,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default
    ) =>
        accessor.Client.SendMessage(
            chatId,
            text,
            parseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken
        );
}
