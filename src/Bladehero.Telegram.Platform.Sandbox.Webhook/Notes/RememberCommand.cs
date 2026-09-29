using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Notes;

// Asks for the note; the sender's next plain text goes to RememberStep, and the prompt's Cancel, bound to this run, to
// CancelNoteStep.
[BotCommand("remember", "Remember a note")]
internal sealed class RememberCommand(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.From is not null && request.Payload.IsCommand("/remember"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        await conversation.StartAsync(Notebook.Flow, "note", token);
        var binding = await conversation.BindAsync(token);

        await client.SendMessage(
            message.Chat,
            "What should I remember?",
            replyMarkup: new InlineKeyboardMarkup().AddButton("Cancel", new CancelNote(), binding),
            cancellationToken: token
        );
    }
}
