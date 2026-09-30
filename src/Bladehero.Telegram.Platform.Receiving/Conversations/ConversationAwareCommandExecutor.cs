using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal sealed class ConversationAwareCommandExecutor(
    IConversation conversation,
    CommandCatalog catalog,
    ParallelTelegramCommandExecutor executor,
    ButtonCatalog buttons,
    IServiceProvider provider
) : ITelegramCommandExecutor
{
    public async Task ExecuteAsync(CommandRequest request, CancellationToken token = default)
    {
        if (await ExecuteStepsAsync(request, token) || await executor.ExecuteRegularAsync(request, token))
        {
            return;
        }

        await RefuseUnclaimedButtonAsync(request, token);
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

    // Answers a typed button's tap that nothing took; hand-written data is left alone.
    private Task RefuseUnclaimedButtonAsync(CommandRequest request, CancellationToken token)
    {
        if (request.Update.CallbackQuery is not { Data: { } data } query || !buttons.TryFind(data, out var codec))
        {
            return Task.CompletedTask;
        }

        var reason = codec.TryDecode(data, out _) ? ButtonRefusalReason.Unclaimed : ButtonRefusalReason.NoLongerActive;
        return provider
            .GetRequiredService<IButtonRefusalHandler>()
            .HandleAsync(new ButtonRefusal(query, request.Client, reason, codec.Type), token);
    }
}
