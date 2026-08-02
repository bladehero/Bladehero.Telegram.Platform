using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform;

public interface ITelegramSender
{
    Task<Message> SendAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = ParseMode.None,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default
    );
}
