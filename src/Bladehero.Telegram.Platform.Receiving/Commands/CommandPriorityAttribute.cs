namespace Bladehero.Telegram.Platform.Receiving.Commands;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPriorityAttribute : Attribute
{
    public CommandPriority Priority { get; }

    /// <summary>
    /// Sets the command's priority, global then group; lower runs first, and unmarked commands run before all.
    /// </summary>
    /// <param name="global">Global priority; 0 is the highest you can set.</param>
    /// <param name="group">
    /// Order within the global priority; <c>null</c> last. Equal priorities run in parallel, up to
    /// <c>ParallelCount</c> at a time.
    /// </param>
    public CommandPriorityAttribute(int global, int? group = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(global);

        if (group.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(group.Value);
        }

        Priority = new CommandPriority(global, group);
    }
}
