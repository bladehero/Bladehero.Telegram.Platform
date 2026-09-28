using System.Collections.Immutable;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

internal sealed class CommandPriorityAccessor((CommandPriority Priority, ITelegramCommand Command)[] commands)
{
    private readonly ImmutableSortedDictionary<CommandPriority, ITelegramCommand[]> _sortedCommandGroups = commands
        .GroupBy(x => x.Priority, CommandPriority.EqualityComparer)
        .ToImmutableSortedDictionary(x => x.Key, x => x.Select(y => y.Command).ToArray(), CommandPriority.Comparer);

    public IReadOnlyDictionary<CommandPriority, ITelegramCommand[]> GetGroups() => _sortedCommandGroups;
}
