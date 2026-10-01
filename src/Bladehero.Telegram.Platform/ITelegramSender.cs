using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform;

/// <summary>
/// Sends the messages the bot starts itself, such as reminders or alerts; replies to an update use the request's
/// client.
/// </summary>
public interface ITelegramSender
{
    /// <summary>
    /// Sends <paramref name="text"/> to the chat, plain unless <paramref name="parseMode"/> says otherwise, and returns
    /// the message as sent.
    /// </summary>
    /// <exception cref="global::Telegram.Bot.Exceptions.ApiRequestException">Telegram refused the message.</exception>
    Task<Message> SendAsync(
        ChatId chatId,
        string text,
        ParseMode parseMode = ParseMode.None,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default
    );
}
