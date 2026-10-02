using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Starts a coffee order, typed or spoken: at the name step when the size is known, at the size card otherwise. Scoped,
// as the conversation it starts is the current update's.
internal sealed class CoffeeOrdering(IConversation conversation, ITelegramMessages messages)
{
    public async Task StartAsync(ITelegramBotClient client, Chat chat, CoffeeSize? size, CancellationToken token)
    {
        await RetireCardInProgressAsync(chat, token);

        // A new run of the flow, so the buttons of an earlier order stop working.
        var step = size is null ? CoffeeFlow.SizeStep : CoffeeFlow.NameStep;
        var order = new CoffeeOrder(CardId: 0, size);
        await conversation.StartAsync(CoffeeFlow.Name, step, order, token);
        var binding = await conversation.BindAsync(token);

        Message sent;
        if (size is { } known)
        {
            await client.SendMessage(chat, $"Size: {known} ✓", cancellationToken: token);
            sent = await client.SendMessage(
                chat,
                "Whose name goes on the cup?",
                replyMarkup: CoffeeFlow.NameKeyboard(binding),
                cancellationToken: token
            );
        }
        else
        {
            sent = await client.SendMessage(
                chat,
                "What size?",
                replyMarkup: CoffeeFlow.SizeKeyboard(binding),
                cancellationToken: token
            );
        }

        await conversation.MoveToAsync(step, order with { CardId = sent.Id }, token);
    }

    // The order a new one replaces loses its buttons, so its card cannot be tapped by mistake.
    private async Task RetireCardInProgressAsync(Chat chat, CancellationToken token)
    {
        // No order in progress, or its card was never sent.
        if (
            await conversation.GetAsync(token) is not { Flow: CoffeeFlow.Name }
            || await conversation.GetDataAsync<CoffeeOrder>(token) is not { CardId: > 0 } previous
        )
        {
            return;
        }

        try
        {
            await messages.ClearKeyboardAsync(new TelegramMessageRef(chat.Id, previous.CardId), token);
        }
        catch (ApiRequestException)
        {
            // A refusal, e.g. a 429, mustn't stop the new order; a card that's gone or bare is fine already.
        }
    }
}
