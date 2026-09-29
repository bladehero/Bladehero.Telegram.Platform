using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

[ConversationStep(CoffeeFlow.Name, CoffeeFlow.NameStep)]
internal sealed class WriteNameStep(IConversation conversation) : MessageCommand
{
    // Declining bot commands lets a /cancel typed here fall through to CancelCommand.
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text is { } text && !text.StartsWith('/'));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var order = (await conversation.GetDataAsync<CoffeeOrder>(token))! with
        {
            CupName = message.Text,
            NameMessageId = message.Id,
        };

        // The prompt's Cancel moves to the confirmation card.
        try
        {
            await client.EditMessageReplyMarkup(
                message.Chat,
                order.CardId,
                replyMarkup: null,
                cancellationToken: token
            );
        }
        catch (ApiRequestException)
        {
            // Already without buttons, or gone.
        }

        var card = await client.SendMessage(
            message.Chat,
            CoffeeFlow.ConfirmText(order),
            replyMarkup: CoffeeFlow.ConfirmKeyboard(message.From!.Id, order.OrderId),
            cancellationToken: token
        );

        await conversation.MoveToAsync(CoffeeFlow.ConfirmStep, order with { CardId = card.Id }, token);
    }
}
