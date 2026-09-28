using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

internal sealed class CancelCommand(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/cancel"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var active = await conversation.GetAsync(token);

        await conversation.EndAsync(token);
        await client.SendMessage(
            message.Chat,
            active is null ? "Nothing to cancel." : $"Cancelled your {active.Flow} order.",
            cancellationToken: token
        );
    }
}
