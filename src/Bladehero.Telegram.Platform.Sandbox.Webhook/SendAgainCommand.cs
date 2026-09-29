using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook;

// The Again button under a reply: sends the reply once more.
public sealed class SendAgainCommand : CallbackQueryCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Data == SendMessageBackCommand.AgainData);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;

        await client.AnswerCallbackQuery(query.Id, "Sent again", cancellationToken: token);
        await client.SendMessage(query.Message!.Chat, query.Message.Text!, cancellationToken: token);
    }
}
