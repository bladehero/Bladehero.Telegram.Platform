using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal sealed class ConversationAwareCommandExecutor(
    IConversation conversation,
    CommandCatalog catalog,
    ParallelTelegramCommandExecutor executor,
    ButtonCatalog buttons,
    ConversationLocks locks,
    ILogger<ConversationAwareCommandExecutor> logger,
    IServiceProvider provider
) : ITelegramCommandExecutor
{
    public async Task ExecuteAsync(CommandRequest request, CancellationToken token = default)
    {
        if (request.Update.CallbackQuery is { Data: { } data } query && buttons.TryFind(data, out var codec))
        {
            await (
                codec.TryDecode(data, out _, out var binding) && binding is { } bound
                    ? ExecuteBoundAsync(request, query, codec, bound, token)
                    : ExecuteUnboundButtonAsync(request, query, codec, data, token)
            );
            return;
        }

        await ExecuteStepsThenRegularAsync(request, stepButtonsOf: null, token);
    }

    // Checked before any step runs, against the tapper and their stored run; the data's user id only picks the refusal.
    private async Task ExecuteBoundAsync(
        CommandRequest request,
        CallbackQuery query,
        ButtonCodec codec,
        ConversationBinding binding,
        CancellationToken token
    )
    {
        if (query.Message is not { } message || message.Date == default || conversation.Key is not { } key)
        {
            await RefuseAsync(request, query, ButtonRefusalReason.NoLongerActive, codec, binding, state: null, token);
            return;
        }

        if (binding.UserId != query.From.Id)
        {
            await RefuseAsync(request, query, ButtonRefusalReason.NotYours, codec, binding, state: null, token);
            return;
        }

        // The conversation is first read under the lock, so a waiting tap sees what the one before it left.
        ConversationState? state;
        bool taken;
        await using (await locks.EnterAsync(key, token))
        {
            state = await conversation.GetAsync(token);
            taken =
                state?.Id == binding.ConversationId
                && TookTheTap((await ExecuteStepsThenRegularAsync(request, stepButtonsOf: null, token)).Ran);
        }

        if (!taken)
        {
            await RefuseAsync(request, query, ButtonRefusalReason.NoLongerActive, codec, binding, state, token);
        }
    }

    // Unbound typed data never reaches its step, or a tap on someone else's card would act on the tapper's own run.
    private async Task ExecuteUnboundButtonAsync(
        CommandRequest request,
        CallbackQuery query,
        ButtonCodec codec,
        string data,
        CancellationToken token
    )
    {
        var (ran, state) = await ExecuteStepsThenRegularAsync(request, stepButtonsOf: codec.Type, token);
        if (TookTheTap(ran))
        {
            return;
        }

        var reason = codec.TryDecode(data, out _) ? ButtonRefusalReason.Unclaimed : ButtonRefusalReason.NoLongerActive;
        await RefuseAsync(request, query, reason, codec, binding: null, state, token);
    }

    // Regular commands run only when no step took the update.
    private async Task<(IReadOnlyList<ITelegramCommand> Ran, ConversationState? State)> ExecuteStepsThenRegularAsync(
        CommandRequest request,
        Type? stepButtonsOf,
        CancellationToken token
    )
    {
        var (ran, state) = await ExecuteStepsAsync(request, stepButtonsOf, token);
        return (ran.Count > 0 ? ran : await executor.ExecuteRegularAsync(request, token), state);
    }

    // The active conversation's steps, without those for `stepButtonsOf` buttons.
    private async Task<(IReadOnlyList<ITelegramCommand> Ran, ConversationState? State)> ExecuteStepsAsync(
        CommandRequest request,
        Type? stepButtonsOf,
        CancellationToken token
    )
    {
        if (catalog.Steps.Count == 0 || await conversation.GetAsync(token) is not { } state)
        {
            return ([], null);
        }

        var candidates = catalog.StepsOf(state).ToArray();
        if (
            stepButtonsOf is not null
            && candidates.FirstOrDefault(x => buttons.ButtonOf(x.Type) == stepButtonsOf) is { } left
        )
        {
            logger.LogWarning(
                "{Button} buttons are handled by the conversation step {Command}, so they must be bound to the "
                    + "conversation: build them with AddButton(text, button, await conversation.BindAsync(token)).",
                stepButtonsOf.Name,
                left.Type.Name
            );
            candidates = [.. candidates.Where(x => buttons.ButtonOf(x.Type) != stepButtonsOf)];
        }

        var steps = candidates.Select(x => x.Resolve(provider)).ToArray();
        return (
            steps.Length > 0 ? await executor.ExecuteAsync(new CommandPriorityAccessor(steps), request, token) : [],
            state
        );
    }

    // Only a callback command takes a tap; a raw command that sees every update doesn't.
    private static bool TookTheTap(IReadOnlyList<ITelegramCommand> ran) => ran.Any(x => x is CallbackQueryCommand);

    private async Task RefuseAsync(
        CommandRequest request,
        CallbackQuery query,
        ButtonRefusalReason reason,
        ButtonCodec codec,
        ConversationBinding? binding,
        ConversationState? state,
        CancellationToken token
    )
    {
        await provider
            .GetRequiredService<IButtonRefusalHandler>()
            .HandleAsync(new ButtonRefusal(query, request.Client, reason, codec.Type, binding, state), token);
        logger.LogDebug("Answered a tap on a {Button} button as {Reason}", codec.Type.Name, reason);
    }
}
