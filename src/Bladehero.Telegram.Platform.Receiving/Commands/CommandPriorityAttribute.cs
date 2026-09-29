namespace Bladehero.Telegram.Platform.Receiving.Commands;

/// <summary>
/// Sets the command's priority, global then group; lower runs first, and unmarked commands run before all.
/// </summary>
/// <remarks>
/// Commands with equal priority run in parallel, up to <c>ParallelCount</c> at a time. An attribute cannot take an
/// <c>int?</c>, so leaving the group out is its own constructor.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPriorityAttribute : Attribute
{
    public CommandPriority Priority { get; }

    /// <param name="global">Global priority; 0 is the highest you can set. The command runs after grouped ones.</param>
    public CommandPriorityAttribute(int global) => Priority = Create(global, group: null);

    /// <param name="global">Global priority; 0 is the highest you can set.</param>
    /// <param name="group">Order within the global priority; lower runs first.</param>
    public CommandPriorityAttribute(int global, int group) => Priority = Create(global, group);

    private static CommandPriority Create(int global, int? group)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(global);

        if (group.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(group.Value);
        }

        return new CommandPriority(global, group);
    }
}
