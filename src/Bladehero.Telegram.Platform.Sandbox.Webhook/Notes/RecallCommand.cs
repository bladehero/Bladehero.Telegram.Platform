using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Notes;

[BotCommand("recall", "Recall your note")]
internal sealed class RecallCommand(Notebook notebook) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.From is not null && request.Payload.IsCommand("/recall"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        return client.SendMessage(
            message.Chat,
            notebook.Read(message.From!.Id) is { } note
                ? $"You asked me to remember: {note}"
                : "Nothing yet — try /remember",
            cancellationToken: token
        );
    }
}
