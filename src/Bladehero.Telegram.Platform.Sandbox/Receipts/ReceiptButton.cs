using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A receipt card's buttons, "receipt:{ownerId}:{receiptId}:{add|discard}".
[ButtonData("receipt")]
internal readonly record struct ReceiptChoice(long OwnerId, string ReceiptId, ReceiptAction Action);

internal enum ReceiptAction
{
    Add,
    Discard,
}

internal sealed class ReceiptButton(PendingReceipts pending, MemberDirectory members, ReceiptHistory history)
    : KnownUserCallbackQueryCommand<Member, ReceiptChoice>
{
    protected override Task<ButtonCheck> CheckAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) =>
        Task.FromResult(
            Parsed.OwnerId == User.UserId
                ? ButtonCheck.Accept
                : ButtonCheck.Reject("This receipt isn't yours.", showAlert: true)
        );

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var (_, receiptId, action) = Parsed;

        // Taken already, by Add, Discard or a tap just before, or lost to a restart.
        if (pending.Take(receiptId) is not { } receipt)
        {
            await client.AnswerCallbackQuery(query.Id, "This receipt is no longer pending.", cancellationToken: token);
            await RemoveButtonsAsync(client, card, token);
            return;
        }

        if (action is ReceiptAction.Discard)
        {
            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await RemoveButtonsAsync(client, card, token);
            return;
        }

        members.Earn(User.UserId, receipt.Points);
        history.Add(receipt);

        await client.AnswerCallbackQuery(query.Id, $"Added {receipt.Points} points", cancellationToken: token);
        await client.EditMessageText(
            card.Chat,
            card.Id,
            $"Receipt: {receipt.Amount} → added {receipt.Points} points ✓",
            cancellationToken: token
        );
    }

    private static async Task RemoveButtonsAsync(ITelegramBotClient client, Message card, CancellationToken token)
    {
        try
        {
            await client.EditMessageReplyMarkup(card.Chat, card.Id, replyMarkup: null, cancellationToken: token);
        }
        catch (ApiRequestException)
        {
            // Already without buttons, or gone.
        }
    }
}
