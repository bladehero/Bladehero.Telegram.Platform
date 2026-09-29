using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Welcome;

// Unmarked, so it runs before the menu and the tip that /start also triggers.
[BotCommand("start", "Start over", Order = 0)]
internal sealed class StartCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/start"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(
            request.Payload.Chat,
            $"Welcome to the coffee shop, {request.Payload.From?.FirstName}!",
            cancellationToken: token
        );
}
