using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Bladehero.Telegram.Platform.TelegramMessageErrors;

namespace Bladehero.Telegram.Platform;

// A fresh message is sent before an old one is removed, so a failed send never loses the card.
internal sealed class TelegramMessages(ITelegramBotClient client, ILogger<TelegramMessages>? logger = null)
    : ITelegramMessages
{
    // An app that only sends may register no logging.
    private readonly ILogger _logger = logger ?? NullLogger<TelegramMessages>.Instance;

    public async Task<TelegramMessageRef> SendAsync(
        ChatId chatId,
        string text,
        ReplyMarkup? replyMarkup = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    ) =>
        TelegramMessageRef.From(
            await client.SendMessage(chatId, text, parseMode, replyMarkup: replyMarkup, cancellationToken: token)
        );

    public async Task<TelegramMessageRef> ShowAsync(
        TelegramMessageRef message,
        string text,
        InlineKeyboardMarkup? keyboard = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    )
    {
        Guard(message);
        if (message.InlineMessageId is { } inlineMessageId)
        {
            // With no chat to send to, it's this message or nothing.
            try
            {
                await client.EditMessageText(
                    inlineMessageId,
                    text,
                    parseMode,
                    replyMarkup: keyboard,
                    cancellationToken: token
                );
            }
            catch (ApiRequestException error) when (IsNotModified(error)) { }

            return message;
        }

        try
        {
            await client.EditMessageText(
                message.ChatId,
                message.MessageId,
                text,
                parseMode,
                replyMarkup: keyboard,
                cancellationToken: token
            );
            return message;
        }
        catch (ApiRequestException error) when (IsNotModified(error))
        {
            return message;
        }
        catch (ApiRequestException error) when (IsGoneForEdit(error))
        {
            _logger.LogDebug(
                "Message {MessageId} in chat {ChatId} is gone; showing it in a fresh one.",
                message.MessageId,
                message.ChatId
            );
            return await SendAsync(message.ChatId, text, keyboard, parseMode, token);
        }
        catch (ApiRequestException error) when (IsUneditable(error))
        {
            _logger.LogDebug(
                "Message {MessageId} in chat {ChatId} can't be edited; showing it in a fresh one.",
                message.MessageId,
                message.ChatId
            );
            return await ReplaceAsync(message, text, keyboard, parseMode, token);
        }
    }

    public async Task<TelegramMessageRef> ReplaceAsync(
        TelegramMessageRef message,
        string text,
        InlineKeyboardMarkup? keyboard = null,
        ParseMode parseMode = ParseMode.None,
        CancellationToken token = default
    )
    {
        Guard(message);
        if (message.InlineMessageId is not null)
        {
            throw new ArgumentException(
                "An inline-mode message can't be replaced: it has no chat to send a fresh one to.",
                nameof(message)
            );
        }

        var fresh = await SendAsync(message.ChatId, text, keyboard, parseMode, token);

        // The fresh one is sent, so the caller's cancellation mustn't strand it without a handle.
        await RemoveAsync(message, CancellationToken.None);
        return fresh;
    }

    public async Task<bool> DeleteAsync(TelegramMessageRef message, CancellationToken token = default)
    {
        Guard(message);
        if (message.InlineMessageId is not null)
        {
            // The Bot API can't delete inline-mode messages.
            await ClearKeyboardIfEditableAsync(message, token);
            return false;
        }

        try
        {
            await client.DeleteMessage(message.ChatId, message.MessageId, token);
            return true;
        }
        catch (ApiRequestException error) when (IsGoneForDelete(error))
        {
            return true;
        }
        catch (ApiRequestException error) when (IsUndeletable(error))
        {
            // After 48 hours, or without the right: at least no stale buttons stay tappable.
            await ClearKeyboardIfEditableAsync(message, token);
            return false;
        }
    }

    public async Task ClearKeyboardAsync(TelegramMessageRef message, CancellationToken token = default)
    {
        Guard(message);
        try
        {
            if (message.InlineMessageId is { } inlineMessageId)
            {
                await client.EditMessageReplyMarkup(inlineMessageId, replyMarkup: null, cancellationToken: token);
            }
            else
            {
                await client.EditMessageReplyMarkup(
                    message.ChatId,
                    message.MessageId,
                    replyMarkup: null,
                    cancellationToken: token
                );
            }
        }
        catch (ApiRequestException error) when (IsNotModified(error) || IsGoneForEdit(error)) { }
    }

    // A message the bot can't edit, such as a user's, keeps what it shows.
    private async Task ClearKeyboardIfEditableAsync(TelegramMessageRef message, CancellationToken token)
    {
        try
        {
            await ClearKeyboardAsync(message, token);
        }
        catch (ApiRequestException error) when (IsUneditable(error)) { }
    }

    // Best effort: a fresh one already shows it, so a failure here is only worth a warning.
    private async Task RemoveAsync(TelegramMessageRef old, CancellationToken token)
    {
        try
        {
            await DeleteAsync(old, token);
        }
        catch (RequestException error) when (!token.IsCancellationRequested)
        {
            _logger.LogWarning(
                error,
                "Couldn't remove message {MessageId} in chat {ChatId}; a fresh one replaced it.",
                old.MessageId,
                old.ChatId
            );
        }
    }

    private static void Guard(TelegramMessageRef message)
    {
        if (message.InlineMessageId is null && message.MessageId == 0)
        {
            throw new ArgumentException("The message reference names no message.", nameof(message));
        }
    }
}
