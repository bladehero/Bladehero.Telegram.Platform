using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.ConfirmStep)]
internal sealed class ConfirmOrderStep(IConversation conversation, MemberDirectory members) : CallbackQueryCommand
{
    private const int PointsPerCoffee = 10;

    protected override async Task<bool> CanHandleAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) =>
        CoffeeButton.Parse(request.Payload.Data) is { Action: CoffeeFlow.ConfirmAction } button
        && await CoffeeFlow.IsCurrentAsync(conversation, button, request.Payload.From.Id, token);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var order = (await conversation.GetDataAsync<CoffeeOrder>(token))!;

        await conversation.EndAsync(token);

        // Anyone can order; members earn points for it.
        members.Earn(query.From.Id, PointsPerCoffee);

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(
            card.Chat,
            card.Id,
            $"Order placed ☕ — a {order.Size} coffee for {order.CupName}.",
            cancellationToken: token
        );
    }
}
