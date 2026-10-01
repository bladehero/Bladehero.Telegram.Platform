using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedMessages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Fixing the cup name by editing its message, while the order waits for Confirm. Other edits change nothing.
[ConversationStep(CoffeeFlow.Name, CoffeeFlow.ConfirmStep)]
internal sealed class EditCupNameStep(IConversation conversation) : EditedMessageCommand
{
    private CoffeeOrder? _order;

    protected override async Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var edited = request.Payload;
        if (edited.Text is not { } text || text.StartsWith('/'))
        {
            return false;
        }

        _order = await conversation.GetDataAsync<CoffeeOrder>(token);
        return _order?.NameMessageId == edited.Id;
    }

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, edited, client) = request;
        var order = _order! with { CupName = edited.Text };

        await client.EditMessageText(
            edited.Chat,
            order.CardId,
            CoffeeFlow.ConfirmText(order),
            replyMarkup: CoffeeFlow.ConfirmKeyboard(await conversation.BindAsync(token)),
            cancellationToken: token
        );
        await conversation.MoveToAsync(CoffeeFlow.ConfirmStep, order, token);
    }
}
