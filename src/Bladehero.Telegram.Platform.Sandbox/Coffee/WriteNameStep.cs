using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
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
        var order = await conversation.GetDataAsync<CoffeeOrder>(token) ?? new CoffeeOrder();
        order = order with { CupName = message.Text };

        await conversation.MoveToAsync(CoffeeFlow.ConfirmStep, order, token);
        await client.SendMessage(
            message.Chat,
            $"A {order.Size} coffee for {order.CupName}. Place the order?",
            replyMarkup: CoffeeFlow.ConfirmKeyboard,
            cancellationToken: token
        );
    }
}
