namespace Bladehero.Telegram.Platform.Testing;

// What the bot logged, and the logged errors that fail an action.
public sealed partial class TelegramTestHost
{
    /// <summary>What the bot logged, from Debug up, since its host was built.</summary>
    public IReadOnlyList<TestLog> Logs => _errors.Logs.Entries;

    /// <summary>Fails an action when the bot logs an error for its update or outside any update; off by default.</summary>
    /// <remarks>
    /// Error and Critical entries count, except the log of an error the action rethrows anyway; the action throws an
    /// <see cref="InvalidOperationException"/> whose inner exception is the entry's. An error logged by work an update
    /// started but didn't wait for fails the next action.
    /// </remarks>
    public bool FailOnErrorLogs { get; set; }
}
