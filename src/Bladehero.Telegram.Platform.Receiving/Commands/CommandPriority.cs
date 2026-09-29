namespace Bladehero.Telegram.Platform.Receiving.Commands;

/// <summary>A command's place in the execution order, set with <see cref="CommandPriorityAttribute"/>.</summary>
public sealed class CommandPriority
{
    internal static readonly CommandPriority Default = new() { Global = -1 };

    internal static readonly IComparer<CommandPriority> Comparer = new CommandPriorityComparer();

    internal static readonly IEqualityComparer<CommandPriority> EqualityComparer =
        new CommandPriorityEqualityComparer();

    /// <summary>
    /// Global priority; lower runs first. 0 is the highest you can set, but unmarked commands (-1) run before all.
    /// </summary>
    public int Global { get; private set; }

    /// <summary>
    /// Order within the same global priority; lower runs first, <c>null</c> last. Commands with equal priority run in
    /// parallel, up to <c>ParallelCount</c> at a time.
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
