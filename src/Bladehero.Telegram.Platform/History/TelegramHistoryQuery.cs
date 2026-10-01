namespace Bladehero.Telegram.Platform.History;

/// <summary>Which entries to read: the latest <see cref="Limit"/> that match every filter set, oldest first.</summary>
public sealed record TelegramHistoryQuery
{
    /// <summary>Only this chat's entries.</summary>
    public long? ChatId { get; init; }

    /// <summary>
    /// Only this message's entries, such as its sending, edits, taps and deletion; needs <see cref="ChatId"/>.
    /// </summary>
    public int? MessageId { get; init; }

    /// <summary>Only this inline-mode message's entries.</summary>
    public string? InlineMessageId { get; init; }

    /// <summary>Only this update and the calls made while handling it.</summary>
    public int? UpdateId { get; init; }

    /// <summary>Only entries from this time on.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Only entries older than this <see cref="TelegramHistoryEntry.Id"/>, to read the page before one already read.
    /// </summary>
    public long? BeforeId { get; init; }

    /// <summary>The most entries to return; 100 unless set.</summary>
    public int Limit { get; init; } = 100;
}
