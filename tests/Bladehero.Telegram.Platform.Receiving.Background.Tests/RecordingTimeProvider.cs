using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

// A FakeTimeProvider that notes each timer's due time: how long the code asked to wait, which a Task.Delay's
// continuation, run later on the thread pool, can't show at the moment the clock moves on.
internal sealed class RecordingTimeProvider : FakeTimeProvider
{
    private readonly List<TimeSpan> _dueTimes = [];

    public IReadOnlyList<TimeSpan> DueTimes
    {
        get
        {
            lock (_dueTimes)
            {
                return [.. _dueTimes];
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_dueTimes)
        {
            _dueTimes.Add(dueTime);
        }

        return base.CreateTimer(callback, state, dueTime, period);
    }
}
