using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Notes;

// The note itself. Declining bot commands lets a /recall sent instead fall through to its command.
[ConversationStep(Notebook.Flow)]
internal sealed class RememberStep(IConversation conversation, Notebook notebook) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text is { } text && !text.StartsWith('/'));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        notebook.Write(message.From!.Id, message.Text!);
        await conversation.EndAsync(token);

        await client.SendMessage(message.Chat, "Got it.", cancellationToken: token);
    }
}
