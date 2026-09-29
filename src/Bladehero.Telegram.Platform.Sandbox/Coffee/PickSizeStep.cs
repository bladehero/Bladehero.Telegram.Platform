using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.SizeStep)]
internal sealed class PickSizeStep(IConversation conversation) : CallbackQueryCommand
{
    private CoffeeSize _size;

    protected override async Task<bool> CanHandleAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    )
    {
        if (CoffeeButton.Parse(request.Payload.Data) is not { Action: CoffeeFlow.SizeAction, Size: { } size } button)
        {
            return false;
        }

        _size = size;
        return await CoffeeFlow.IsCurrentAsync(conversation, button, request.Payload.From.Id, token);
    }

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var order = (await conversation.GetDataAsync<CoffeeOrder>(token))!;

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(card.Chat, card.Id, $"Size: {_size} ✓", cancellationToken: token);
        var prompt = await client.SendMessage(
            card.Chat,
            "Whose name goes on the cup?",
            replyMarkup: CoffeeFlow.NameKeyboard(query.From.Id, order.OrderId),
            cancellationToken: token
        );

        await conversation.MoveToAsync(CoffeeFlow.NameStep, order with { Size = _size, CardId = prompt.Id }, token);
    }
}
