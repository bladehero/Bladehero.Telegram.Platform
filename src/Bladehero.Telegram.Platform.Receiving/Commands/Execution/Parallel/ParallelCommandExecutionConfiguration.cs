namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;

/// <summary>How the commands of one priority run in parallel; set it with <c>services.Configure</c>.</summary>
public sealed class ParallelCommandExecutionConfiguration
{
    /// <summary>
    /// How many commands of a batch run at once, in chunks one after another: 5 by default; <c>null</c> runs the
    /// batch as one unbounded chunk.
    /// </summary>
    /// <remarks>
    /// Set it to 1 when commands share a scoped dependency that isn't thread-safe, such as a <c>DbContext</c>. A
    /// value below 1 fails every update.
    /// </remarks>
    public int? ParallelCount { get; set; } = 5;
}
