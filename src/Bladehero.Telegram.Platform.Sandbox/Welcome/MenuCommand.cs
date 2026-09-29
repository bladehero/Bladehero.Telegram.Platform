using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Welcome;

// Lists the menu the bot publishes, so /help never drifts from it. After the greeting on /start, before the tip.
[CommandPriority(1, 0)]
[BotCommand("help", "What I can do")]
internal sealed class MenuCommand(IBotCommandMenu menu) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/start") || request.Payload.IsCommand("/help"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(
            request.Payload.Chat,
            string.Join(
                "\n",
                ["Here is what I can do:", .. menu.Commands.Select(x => $"/{x.Command} - {x.Description}")]
            ),
            cancellationToken: token
        );
}
