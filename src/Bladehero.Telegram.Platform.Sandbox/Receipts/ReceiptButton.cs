using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A receipt card's "receipt:{ownerId}:{receiptId}:{add|discard}" buttons.
internal sealed class ReceiptButton(PendingReceipts pending, MemberDirectory members, ReceiptHistory history)
    : KnownUserCallbackQueryCommand<Member, (long OwnerId, string ReceiptId, string Action)>
{
    public const string Add = "add";
    public const string Discard = "discard";

    private const string Prefix = "receipt";

    public static string Data(long ownerId, string receiptId, string action) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{ownerId}:{receiptId}:{action}");

    protected override (long OwnerId, string ReceiptId, string Action)? Parse(string data) =>
        data.Split(':') is [Prefix, var owner, var receiptId, var action and (Add or Discard)]
        && long.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId)
            ? (ownerId, receiptId, action)
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var card = query.Message!;
        var (ownerId, receiptId, action) = Parsed;

        if (ownerId != User.UserId)
        {
            await client.AnswerCallbackQuery(
                query.Id,
                "This receipt isn't yours.",
                showAlert: true,
                cancellationToken: token
            );
            return;
        }

        // Taken already, by Add, Discard or a tap just before, or lost to a restart.
        if (pending.Take(receiptId) is not { } receipt)
        {
            await client.AnswerCallbackQuery(query.Id, "This receipt is no longer pending.", cancellationToken: token);
            await RemoveButtonsAsync(client, card, token);
            return;
        }

        if (action is Discard)
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
