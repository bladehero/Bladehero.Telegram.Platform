using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// "/coffee", or "/coffee large" to skip the size step.
[BotCommand("coffee", "Order a coffee", Order = 1)]
internal sealed class OrderCoffeeCommand(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.From is not null && request.Payload.IsCommand("/coffee"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var ownerId = message.From!.Id;
        var orderId = CoffeeFlow.NewOrderId();

        await RetireCardInProgressAsync(client, message.Chat, token);

        if (CoffeeFlow.ParseSize(message.ArgumentsOf("/coffee")) is { } size)
        {
            await client.SendMessage(message.Chat, $"Size: {size} ✓", cancellationToken: token);
            var prompt = await client.SendMessage(
                message.Chat,
                "Whose name goes on the cup?",
                replyMarkup: CoffeeFlow.NameKeyboard(ownerId, orderId),
                cancellationToken: token
            );
            await conversation.StartAsync(
                CoffeeFlow.Name,
                CoffeeFlow.NameStep,
                new CoffeeOrder(orderId, prompt.Id, size),
                token
            );
            return;
        }

        var card = await client.SendMessage(
            message.Chat,
            "What size?",
            replyMarkup: CoffeeFlow.SizeKeyboard(ownerId, orderId),
            cancellationToken: token
        );
        await conversation.StartAsync(CoffeeFlow.Name, CoffeeFlow.SizeStep, new CoffeeOrder(orderId, card.Id), token);
    }

    // The order a new /coffee replaces loses its buttons, so its card cannot be tapped by mistake.
    private async Task RetireCardInProgressAsync(ITelegramBotClient client, Chat chat, CancellationToken token)
    {
        if (
            await conversation.GetAsync(token) is not { Flow: CoffeeFlow.Name }
            || await conversation.GetDataAsync<CoffeeOrder>(token) is not { } previous
        )
        {
            return;
        }

        try
        {
            await client.EditMessageReplyMarkup(chat, previous.CardId, replyMarkup: null, cancellationToken: token);
        }
        catch (ApiRequestException)
        {
            // It may have no buttons left, or be gone; either way nothing can be tapped on it.
        }
    }
}
