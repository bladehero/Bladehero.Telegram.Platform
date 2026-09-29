using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Notes;

// The prompt's Cancel, "note-cancel@{userId}.{run}".
[ButtonData("note-cancel")]
internal readonly record struct CancelNote();

// Bound to the run, so after the note is saved, or on a second tap at once, the library answers instead.
[ConversationStep(Notebook.Flow)]
internal sealed class CancelNoteStep(IConversation conversation) : CallbackQueryCommand<CancelNote>
{
    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var prompt = query.Message!;

        await conversation.EndAsync(token);
        await client.AnswerCallbackQuery(query.Id, "Cancelled.", cancellationToken: token);
        await client.EditMessageText(prompt.Chat, prompt.Id, "Nothing remembered.", cancellationToken: token);
    }
}
