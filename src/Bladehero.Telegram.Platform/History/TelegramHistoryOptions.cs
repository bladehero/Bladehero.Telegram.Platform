namespace Bladehero.Telegram.Platform.History;

/// <summary>How the history is recorded.</summary>
public sealed class TelegramHistoryOptions
{
    /// <summary>Keeps each entry's Bot API JSON; turn it off to keep only the other fields. On by default.</summary>
    public bool KeepJson { get; set; } = true;

    /// <summary>
    /// Changes or drops each entry before it's stored: return it, changed with <c>with</c> if need be, or null to drop
    /// it.
    /// </summary>
    public Func<TelegramHistoryEntry, TelegramHistoryEntry?>? Filter { get; set; }

    /// <summary>How many entries may wait to be stored; more are dropped with a warning. 10 000 unless set.</summary>
    public int QueueCapacity { get; set; } = 10_000;
}
