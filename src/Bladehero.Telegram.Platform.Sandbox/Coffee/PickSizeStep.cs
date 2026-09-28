using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.SizeStep)]
internal sealed class PickSizeStep(IConversation conversation) : CallbackQueryCommand<CoffeeSize>
{
    protected override CoffeeSize? Parse(string data) => CoffeeFlow.ParseSize(data);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var order = await conversation.GetDataAsync<CoffeeOrder>(token) ?? new CoffeeOrder();

        await conversation.MoveToAsync(CoffeeFlow.NameStep, order with { Size = Parsed }, token);

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(card.Chat, card.Id, $"Size: {Parsed} ✓", cancellationToken: token);
        await client.SendMessage(card.Chat, "Whose name goes on the cup?", cancellationToken: token);
    }
}
