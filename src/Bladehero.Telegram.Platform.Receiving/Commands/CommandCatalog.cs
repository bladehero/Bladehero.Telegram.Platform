using Bladehero.Telegram.Platform.Receiving.Conversations;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

internal sealed class CommandCatalog
{
    public CommandCatalog(IEnumerable<CatalogedCommand> commands)
    {
        var all = commands.ToArray();
        Regular = [.. all.Where(x => x.Step is null)];
        Steps = [.. all.Where(x => x.Step is not null)];
    }

    public IReadOnlyList<CatalogedCommand> Regular { get; }

    public IReadOnlyList<CatalogedCommand> Steps { get; }

    public IEnumerable<CatalogedCommand> StepsOf(ConversationState state) =>
        Steps.Where(x => x.Step?.Matches(state) is true);
}

internal sealed record CatalogedCommand(Type Type, CommandPriority Priority, ConversationStepAttribute? Step)
{
    public (CommandPriority Priority, ITelegramCommand Command) Resolve(IServiceProvider provider) =>
        (Priority, (ITelegramCommand)provider.GetRequiredService(Type));
}
