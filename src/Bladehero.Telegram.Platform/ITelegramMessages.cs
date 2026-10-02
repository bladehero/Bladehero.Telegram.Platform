using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform;

/// <summary>
/// Sends the bot's messages and changes them later by <see cref="TelegramMessageRef"/>, following Telegram's rules on
/// what can be edited and deleted.
/// </summary>
/// <remarks>A <c>default</c> <see cref="TelegramMessageRef"/> names no message, and every method refuses it.</remarks>
public interface ITelegramMessages
{
    /// <summary>
    /// Sends <paramref name="text"/>, with a keyboard or other reply markup if given, and returns the message.
    /// </summary>
    /// <param name="chatId">The chat to send to.</param>
    /// <param name="text">The message's text.</param>
    /// <param name="replyMarkup">
    /// An inline or reply keyboard, a <c>ForceReply</c> or a keyboard removal, if any.
    /// </param>
    /// <param name="parseMode">How <paramref name="text"/> is formatted; plain unless set.</param>
    /// <param name="token">Cancels the call.</param>
    Task<TelegramMessageRef> SendAsync(
        ChatId chatId,
        string text,
        ReplyMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    );

    /// <summary>
    /// Shows <paramref name="text"/> and <paramref name="keyboard"/> on the message by editing it; a message that can't be
    /// edited any more is replaced with a fresh one. Returns the message showing it now.
    /// </summary>
    /// <param name="message">The message to show it on.</param>
    /// <param name="text">The text to show.</param>
    /// <param name="keyboard">The buttons to show under it, if any.</param>
    /// <param name="parseMode">How <paramref name="text"/> is formatted; plain unless set.</param>
    /// <param name="token">Cancels the call.</param>
    Task<TelegramMessageRef> ShowAsync(
        TelegramMessageRef message,
        string text,
        InlineKeyboardMarkup? keyboard = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    );

    /// <summary>
    /// Sends <paramref name="text"/> as a fresh message at the bottom of the chat, then removes the old one. Returns the
    /// new message.
    /// </summary>
    /// <param name="message">The message to replace.</param>
    /// <param name="text">The fresh message's text.</param>
    /// <param name="keyboard">The buttons under it, if any.</param>
    /// <param name="parseMode">How <paramref name="text"/> is formatted; plain unless set.</param>
    /// <param name="token">Cancels the call.</param>
    /// <exception cref="ArgumentException">An inline-mode message, which has no chat to send to.</exception>
    Task<TelegramMessageRef> ReplaceAsync(
        TelegramMessageRef message,
        string text,
        InlineKeyboardMarkup? keyboard = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    );

    /// <summary>
    /// Deletes the message, or takes its keyboard off when Telegram won't delete it (after 48 hours, or inline) and it
    /// can be edited; true when it's gone, false when it stays.
    /// </summary>
    /// <param name="message">The message to delete.</param>
    /// <param name="token">Cancels the call.</param>
    Task<bool> DeleteAsync(TelegramMessageRef message, CancellationToken token = default);

    /// <summary>Shows <paramref name="keyboard"/> on the message and keeps its text; one that's gone is fine.</summary>
    /// <param name="message">The message to show it on.</param>
    /// <param name="keyboard">The buttons to show; none when <c>null</c>.</param>
    /// <param name="token">Cancels the call.</param>
    Task ShowKeyboardAsync(
        TelegramMessageRef message,
        InlineKeyboardMarkup? keyboard,
        CancellationToken token = default
    );

    /// <summary>Takes the message's keyboard off and keeps its text; one without a keyboard, or gone, is fine.</summary>
    /// <param name="message">The message to take the keyboard off.</param>
    /// <param name="token">Cancels the call.</param>
    Task ClearKeyboardAsync(TelegramMessageRef message, CancellationToken token = default);
}
