using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal sealed class ConversationAwareCommandExecutor(
    IConversation conversation,
    CommandCatalog catalog,
    ParallelTelegramCommandExecutor executor,
    IServiceProvider provider
) : ITelegramCommandExecutor
{
    public async Task ExecuteAsync(CommandRequest request, CancellationToken token = default)
    {
        if (await ExecuteStepsAsync(request, token))
        {
            return;
        }

        await executor.ExecuteAsync(request, token);
    }

    private async Task<bool> ExecuteStepsAsync(CommandRequest request, CancellationToken token)
    {
        if (catalog.Steps.Count == 0 || await conversation.GetAsync(token) is not { } state)
        {
            return false;
        }

        var steps = catalog.StepsOf(state).Select(x => x.Resolve(provider)).ToArray();
        return steps.Length > 0 && await executor.ExecuteAsync(new CommandPriorityAccessor(steps), request, token);
    }
}
