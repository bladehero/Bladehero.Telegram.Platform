namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;

public sealed class ParallelCommandExecutionConfiguration
{
    public int? ParallelCount { get; set; } = 5;
}
