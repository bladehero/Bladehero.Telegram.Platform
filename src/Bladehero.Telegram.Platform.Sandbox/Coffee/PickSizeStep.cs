using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.SizeStep)]
internal sealed class PickSizeStep(IConversation conversation) : CallbackQueryCommand<PickSize>
{
    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var size = Parsed.Size;
        var order = (await conversation.GetDataAsync<CoffeeOrder>(token))!;

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(card.Chat, card.Id, $"Size: {size} ✓", cancellationToken: token);
        var prompt = await client.SendMessage(
            card.Chat,
            "Whose name goes on the cup?",
            replyMarkup: CoffeeFlow.NameKeyboard(await conversation.BindAsync(token)),
            cancellationToken: token
        );

        await conversation.MoveToAsync(CoffeeFlow.NameStep, order with { Size = size, CardId = prompt.Id }, token);
    }
}
