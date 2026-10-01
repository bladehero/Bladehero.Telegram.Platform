using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Testing;

// What the bot logged, and the logged errors that fail an action.
public sealed partial class TelegramTestHost
{
    private bool _failOnErrorLogs;

    /// <summary>What the bot logged, from Debug up, since its host was built.</summary>
    public IReadOnlyList<TestLog> Logs => _errors.Logs.Entries;

    /// <summary>Fails an action when the bot logs an error for its update or outside any update; off by default.</summary>
    /// <remarks>
    /// Counts from when it's turned on. Error and Critical entries count, except the log of an error the action
    /// rethrows anyway; the action throws an <see cref="InvalidOperationException"/> whose inner exception is the
    /// entry's. An error logged by work an update started but didn't wait for fails the next action.
    /// </remarks>
    public bool FailOnErrorLogs
    {
        get => _failOnErrorLogs;
        set
        {
            if (value && !_failOnErrorLogs)
            {
                // Entries logged while it was off don't count.
                _errors.Logs.Claim(x => x.Level is LogLevel.Error or LogLevel.Critical);
            }

            _failOnErrorLogs = value;
        }
    }
}
