namespace Bladehero.Telegram.Platform.Receiving.Commands;

public sealed class CommandPriority
{
    internal static readonly CommandPriority Default = new() { Global = -1 };

    internal static readonly IComparer<CommandPriority> Comparer = new CommandPriorityComparer();

    internal static readonly IEqualityComparer<CommandPriority> EqualityComparer =
        new CommandPriorityEqualityComparer();

    /// <summary>
    /// Global level of priority <br/>
    /// The lower Global value - the higher priority: <br/>
    /// 0 - is the highest priority
    /// </summary>
    public int Global { get; private set; }

    /// <summary>
    /// Group level of priority <br/>
    /// If two (or more) items have the same global level of priority
    /// Then the order of the execution is defined by this Group value <br/>
    /// The lower Group value - the higher priority: <br/>
    /// 0 - is the highest priority <br/>
    /// null - means no group priority defined, and it will be the lowest priority
    /// in that case if parallel execution enabled it could invoke both (or more in parallel)
    /// </summary>
    public int? Group { get; private set; }

    private CommandPriority() { }

    internal CommandPriority(int global, int? group = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(global);

        if (group.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(group.Value);
        }

        Global = global;
        Group = group;
    }

    private sealed class CommandPriorityComparer : IComparer<CommandPriority>
    {
        public int Compare(CommandPriority? x, CommandPriority? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (y is null)
            {
                return -1;
            }

            if (x is null)
            {
                return 1;
            }

            var globalComparison = x.Global.CompareTo(y.Global);
            if (globalComparison != 0)
            {
                return globalComparison;
            }

            if (x.Group.HasValue)
            {
                if (y.Group.HasValue)
                {
                    return Comparer<int>.Default.Compare(x.Group.Value, y.Group.Value);
                }

                return -1;
            }

            if (y.Group.HasValue)
            {
                return 1;
            }

            return 0;
        }
    }

    private sealed class CommandPriorityEqualityComparer : IEqualityComparer<CommandPriority>
    {
        public bool Equals(CommandPriority? x, CommandPriority? y) => Comparer.Compare(x, y) == 0;

        public int GetHashCode(CommandPriority obj) => HashCode.Combine(obj.Global, obj.Group);
    }
}
