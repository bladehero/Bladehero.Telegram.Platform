using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Tests;

// A FakeTimeProvider whose timers run in the execution context they were created in, as the system's do.
internal sealed class ContextCapturingTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider _time = new();

    public void Advance(TimeSpan delta) => _time.Advance(delta);

    public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Null when the creator suppressed the flow.
        var context = ExecutionContext.Capture();
        return _time.CreateTimer(
            x =>
            {
                if (context is null)
                {
                    callback(x);
                }
                else
                {
                    ExecutionContext.Run(context, y => callback(y), x);
                }
            },
            state,
            dueTime,
            period
        );
    }
}
