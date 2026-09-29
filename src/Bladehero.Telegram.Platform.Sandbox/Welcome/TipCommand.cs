using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Welcome;

// No group, so after the menu, which shares its global priority.
[CommandPriority(1)]
internal sealed class TipCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/start"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(
            request.Payload.Chat,
            "Tip: members earn 10 points a coffee — /join",
            cancellationToken: token
        );
}
