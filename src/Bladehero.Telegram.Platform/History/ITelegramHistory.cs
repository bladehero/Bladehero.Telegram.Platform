namespace Bladehero.Telegram.Platform.History;

/// <summary>The bot's history, to read back.</summary>
public interface ITelegramHistory
{
    /// <summary>
    /// The entries that match <paramref name="query"/>, oldest first; it includes every entry recorded before the call,
    /// waiting up to 2 seconds for them to be stored, and not at all while the store is failing.
    /// </summary>
    /// <exception cref="ArgumentException">A <c>MessageId</c> without a <c>ChatId</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A <c>Limit</c> below 1.</exception>
    Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(TelegramHistoryQuery query, CancellationToken token = default);

    /// <summary>Waits until every entry recorded so far is stored.</summary>
    Task FlushAsync(CancellationToken token = default);
}
