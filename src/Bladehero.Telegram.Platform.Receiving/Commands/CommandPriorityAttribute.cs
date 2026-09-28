namespace Bladehero.Telegram.Platform.Receiving.Commands;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPriorityAttribute : Attribute
{
    public CommandPriority Priority { get; }

    /// <summary>
    /// Defines command priority based on provided integers in format Global:Group
    /// </summary>
    /// <param name="global">
    /// Global level of priority <br/>
    /// The lower Global value - the higher priority: <br/>
    /// 0 - is the highest priority
    /// </param>
    /// <param name="group">
    /// Group level of priority <br/>
    /// If two (or more) items have the same global level of priority
    /// Then the order of the execution is defined by this Group value <br/>
    /// The lower Group value - the higher priority: <br/>
    /// 0 - is the highest priority <br/>
    /// null - means no group priority defined, and it will be the lowest priority
    /// in that case if parallel execution enabled it could invoke both (or more in parallel)
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
