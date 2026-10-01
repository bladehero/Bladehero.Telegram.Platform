namespace Bladehero.Telegram.Platform.History;

/// <summary>Where the history is kept; implement it to keep the history in storage of your own.</summary>
public interface ITelegramHistoryStore
{
    /// <summary>
    /// Stores <paramref name="entries"/>, oldest first, giving each an increasing <see cref="TelegramHistoryEntry.Id"/>.
    /// </summary>
    /// <remarks>One background writer calls it, one batch at a time.</remarks>
    Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token);

    /// <summary>The latest <c>Limit</c> entries that match <paramref name="query"/>, oldest first.</summary>
    Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(TelegramHistoryQuery query, CancellationToken token);
}
