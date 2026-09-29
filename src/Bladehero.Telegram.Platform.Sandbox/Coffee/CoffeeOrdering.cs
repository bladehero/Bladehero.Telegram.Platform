using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Starts a coffee order, typed or spoken: at the name step when the size is known, at the size card otherwise. Scoped,
// as the conversation it starts is the current update's.
internal sealed class CoffeeOrdering(IConversation conversation)
{
    public async Task StartAsync(
        ITelegramBotClient client,
        Chat chat,
        long ownerId,
        CoffeeSize? size,
        CancellationToken token
    )
    {
        var orderId = CoffeeFlow.NewOrderId();

        await RetireCardInProgressAsync(client, chat, token);

        if (size is { } known)
        {
            await client.SendMessage(chat, $"Size: {known} ✓", cancellationToken: token);
            var prompt = await client.SendMessage(
                chat,
                "Whose name goes on the cup?",
                replyMarkup: CoffeeFlow.NameKeyboard(ownerId, orderId),
                cancellationToken: token
            );
            await conversation.StartAsync(
                CoffeeFlow.Name,
                CoffeeFlow.NameStep,
                new CoffeeOrder(orderId, prompt.Id, known),
                token
            );
            return;
        }

        var card = await client.SendMessage(
            chat,
            "What size?",
            replyMarkup: CoffeeFlow.SizeKeyboard(ownerId, orderId),
            cancellationToken: token
        );
        await conversation.StartAsync(CoffeeFlow.Name, CoffeeFlow.SizeStep, new CoffeeOrder(orderId, card.Id), token);
    }

    // The order a new one replaces loses its buttons, so its card cannot be tapped by mistake.
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
