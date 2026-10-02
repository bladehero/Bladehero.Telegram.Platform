using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform;

/// <summary>
/// A message the bot sent, to change later: a chat and a message id, or an inline-mode message's id. A value to store.
/// </summary>
public readonly record struct TelegramMessageRef
{
    /// <summary>A message in a chat.</summary>
    /// <param name="chatId">The chat it's in.</param>
    /// <param name="messageId">Its id in that chat.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chatId"/> is 0, or <paramref name="messageId"/> isn't positive.
    /// </exception>
    public TelegramMessageRef(long chatId, int messageId)
    {
        if (chatId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chatId), chatId, "No chat has the id 0.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(messageId);
        ChatId = chatId;
        MessageId = messageId;
    }

    /// <summary>An inline-mode message, which has no chat.</summary>
    /// <param name="inlineMessageId">Its inline message id.</param>
    /// <exception cref="ArgumentException"><paramref name="inlineMessageId"/> is empty.</exception>
    public TelegramMessageRef(string inlineMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inlineMessageId);
        InlineMessageId = inlineMessageId;
    }

    /// <summary>The chat; 0 for an inline-mode message.</summary>
    public long ChatId { get; }

    /// <summary>The message's id in its chat; 0 for an inline-mode message.</summary>
    public int MessageId { get; }

    /// <summary>The inline-mode message's id; null for a message in a chat.</summary>
    public string? InlineMessageId { get; }

    /// <summary>The message as sent, e.g. a result of <c>SendMessage</c>.</summary>
    /// <param name="message">The message.</param>
    public static TelegramMessageRef From(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new TelegramMessageRef(message.Chat.Id, message.Id);
    }

    /// <summary>The message a button was tapped on, in a chat or inline.</summary>
    /// <param name="query">The tap.</param>
    /// <exception cref="ArgumentException">The query has neither, as for a game button.</exception>
    public static TelegramMessageRef From(CallbackQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query switch
        {
            { Message: { } message } => new TelegramMessageRef(message.Chat.Id, message.Id),
            { InlineMessageId: { } inlineMessageId } => new TelegramMessageRef(inlineMessageId),
            _ => throw new ArgumentException("The tap has no message to change, as for a game button.", nameof(query)),
        };
    }
}
