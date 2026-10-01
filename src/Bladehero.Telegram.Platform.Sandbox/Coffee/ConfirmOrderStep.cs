using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Sandbox.Barista;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.ConfirmStep)]
internal sealed class ConfirmOrderStep(IConversation conversation, MemberDirectory members, OrderQueue orders)
    : CallbackQueryCommand<ConfirmOrder>
{
    private const int PointsPerCoffee = 10;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var order = (await conversation.GetDataAsync<CoffeeOrder>(token))!;

        await conversation.EndAsync(token);

        // Anyone can order; members earn points for it.
        members.Earn(query.From.Id, PointsPerCoffee);
        orders.Queue(new PlacedOrder(card.Chat.Id, order.Size!.Value, order.CupName!));

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(
            card.Chat,
            card.Id,
            $"Order placed ☕ — a {order.Size} coffee for {order.CupName}.",
            cancellationToken: token
        );
    }
}
