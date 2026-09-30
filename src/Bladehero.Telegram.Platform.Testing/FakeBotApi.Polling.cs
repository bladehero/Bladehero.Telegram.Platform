namespace Bladehero.Telegram.Platform.Testing;

// Two pollers at once: as in Telegram, a new poll that has to wait ends the older waiting one with 409.
public sealed partial class FakeBotApi
{
    private const string TerminatedByOtherPoll =
        "Conflict: terminated by other getUpdates request; make sure that only one bot instance is running";

    internal bool ConflictedPolling => _updates.Superseded;

    internal bool PollWaiting => _updates.Waiting;
}
