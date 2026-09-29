namespace Bladehero.Telegram.Platform.Receiving.Commands;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPriorityAttribute : Attribute
{
    public CommandPriority Priority { get; }

    /// <summary>Sets the command's priority as global, then group; lower runs first.</summary>
    /// <param name="global">Global priority; 0 is the highest.</param>
    /// <param name="group">Order within the same global priority; <c>null</c> runs last, possibly in parallel.</param>
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
