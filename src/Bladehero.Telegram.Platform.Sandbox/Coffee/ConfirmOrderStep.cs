using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.ConfirmStep)]
internal sealed class ConfirmOrderStep(IConversation conversation) : CallbackQueryCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Data == CoffeeFlow.ConfirmButton);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var order = await conversation.GetDataAsync<CoffeeOrder>(token) ?? new CoffeeOrder();

        await conversation.EndAsync(token);

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(
            card.Chat,
            card.Id,
            $"Order placed ☕ — a {order.Size} coffee for {order.CupName}.",
            cancellationToken: token
        );
    }
}
