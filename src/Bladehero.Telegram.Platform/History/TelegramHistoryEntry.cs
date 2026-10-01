using System.Text;

namespace Bladehero.Telegram.Platform.History;

/// <summary>One event between the bot and Telegram: an update it got, or a Bot API call it made.</summary>
/// <remarks>Entries never change: an edit or a deletion is an entry of its own.</remarks>
public sealed record TelegramHistoryEntry
{
    /// <summary>The store's id, increasing in the order entries were recorded; 0 until stored.</summary>
    public long Id { get; init; }

    /// <summary>When the bot got the update, or when the call finished.</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>An update Telegram sent, or a call the bot made.</summary>
    public TelegramHistoryDirection Direction { get; init; }

    /// <summary>
    /// The update's type, such as <c>message</c>, or the call's method, such as <c>sendMessage</c>, as the Bot API
    /// names them.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>The update's id; for a call, the update the bot was handling when it made it, if any.</summary>
    public int? UpdateId { get; init; }

    /// <summary>The chat it happened in.</summary>
    public long? ChatId { get; init; }

    /// <summary>The user who sent the update, or the user a call is about.</summary>
    public long? UserId { get; init; }

    /// <summary>The message it's about, within <see cref="ChatId"/>.</summary>
    public int? MessageId { get; init; }

    /// <summary>The inline-mode message it's about; such a message has no chat.</summary>
    public string? InlineMessageId { get; init; }

    /// <summary>The text or caption as Telegram shows it, a button's data, or an inline query.</summary>
    public string? Text { get; init; }

    /// <summary>The file's id, such as a photo's largest size; never its bytes.</summary>
    public string? FileId { get; init; }

    /// <summary>The file's name, when Telegram gives one.</summary>
    public string? FileName { get; init; }

    /// <summary>Telegram's error code for a call it refused.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>Why a call failed: Telegram's description or the exception's message; null when it succeeded.</summary>
    public string? Error { get; init; }

    /// <summary>The update, or the call's request and result, as Bot API JSON; null when not kept.</summary>
    public string? Json { get; init; }

    /// <summary>
    /// The kind, the text and a failed call's error, e.g.
    /// <c>sendMessage: Hi (403 Forbidden: bot was blocked by the user)</c>.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder(Kind);
        if (!string.IsNullOrEmpty(Text))
        {
            text.Append(": ").Append(Text);
        }

        if (Error is not null)
        {
            text.Append(ErrorCode is { } code ? $" ({code} {Error})" : $" ({Error})");
        }

        return text.ToString();
    }
}
