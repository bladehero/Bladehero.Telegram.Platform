using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// A step of every coffee step: the order's Cancel button, wherever it is shown.
[ConversationStep(CoffeeFlow.Name)]
internal sealed class CancelOrderStep(IConversation conversation) : CallbackQueryCommand<CancelOrder>
{
    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;

        await conversation.EndAsync(token);

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(card.Chat, card.Id, "Order cancelled.", cancellationToken: token);
    }
}
