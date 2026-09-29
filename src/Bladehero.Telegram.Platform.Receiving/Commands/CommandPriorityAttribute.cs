namespace Bladehero.Telegram.Platform.Receiving.Commands;

/// <summary>
/// Sets the command's priority, global then group; lower runs first, and unmarked commands run before all.
/// </summary>
/// <remarks>
/// Commands with equal priority run in parallel, up to <c>ParallelCount</c> at a time. An attribute cannot take an
/// <c>int?</c>, so leaving the group out is its own constructor. A negative value compiles but fails when the
/// attribute is read, at startup.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPriorityAttribute : Attribute
{
    public CommandPriority Priority { get; }

    /// <summary>Runs after the commands with the same global priority that set a group.</summary>
    /// <param name="global">Global priority; 0 is the highest you can set.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="global"/> is negative.</exception>
    public CommandPriorityAttribute(int global) => Priority = new CommandPriority(global);

    /// <param name="global">Global priority; 0 is the highest you can set.</param>
    /// <param name="group">Order within the global priority; lower runs first.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either value is negative.</exception>
    public CommandPriorityAttribute(int global, int group) => Priority = new CommandPriority(global, group);
}
