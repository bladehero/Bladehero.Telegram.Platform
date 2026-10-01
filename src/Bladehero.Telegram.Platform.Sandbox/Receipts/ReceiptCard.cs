using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// Turns a message, the notice of a receipt or the prompt of an album, into what the reader made of it.
internal sealed class ReceiptCard(PendingReceipts pending)
{
    public async Task ShowAsync(
        ITelegramBotClient client,
        Chat chat,
        int messageId,
        long ownerId,
        DateTime sentAt,
        ReceiptReading reading,
        CancellationToken token
    )
    {
        if (reading.Error is { } error)
        {
            await client.EditMessageText(chat, messageId, error, cancellationToken: token);
            return;
        }

        var receipt = new Receipt(ownerId, sentAt, reading.Total, reading.Currency);
        var id = pending.Add(receipt);

        await client.EditMessageText(
            chat,
            messageId,
            $"Receipt: {receipt.Amount} → {receipt.Points} points.",
            replyMarkup: new InlineKeyboardMarkup()
                .AddButton($"Add {receipt.Points} points", new ReceiptChoice(ownerId, id, ReceiptAction.Add))
                .AddButton("Discard", new ReceiptChoice(ownerId, id, ReceiptAction.Discard)),
            cancellationToken: token
        );
    }
}
