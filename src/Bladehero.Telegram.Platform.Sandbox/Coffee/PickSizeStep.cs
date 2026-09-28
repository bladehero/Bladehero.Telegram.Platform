using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.SizeStep)]
internal sealed class PickSizeStep(IConversation conversation) : CallbackQueryCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Data?.StartsWith(CoffeeFlow.SizeButton) is true);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var size = query.Data![CoffeeFlow.SizeButton.Length..];
        var order = await conversation.GetDataAsync<CoffeeOrder>(token) ?? new CoffeeOrder();

        await conversation.MoveToAsync(CoffeeFlow.NameStep, order with { Size = size }, token);

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(card.Chat, card.Id, $"Size: {size} ✓", cancellationToken: token);
        await client.SendMessage(card.Chat, "Whose name goes on the cup?", cancellationToken: token);
    }
}
