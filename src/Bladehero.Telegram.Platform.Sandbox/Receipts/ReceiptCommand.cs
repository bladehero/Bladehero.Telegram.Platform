using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A member's receipt sent on its own, not in an album: a photo, or a PDF or image as a file.
internal sealed class ReceiptCommand(IReceiptReader reader, ReceiptCard card) : KnownUserCommand<Member>
{
    protected override bool Matches(Message message) => message.MediaGroupId is null && ReceiptFiles.IsReceipt(message);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var notice = await client.SendMessage(message.Chat, "Reading your receipt…", cancellationToken: token);

        if (await ReceiptFiles.DownloadAsync(client, [ReceiptFiles.FileOf(message)], token) is not { } pages)
        {
            await client.EditMessageText(message.Chat, notice.Id, ReceiptFiles.TooBig, cancellationToken: token);
            return;
        }

        // A reader that throws fails the update, for the error handler to deal with.
        var reading = await reader.ReadAsync(pages, message.Caption, token);
        await card.ShowAsync(client, message.Chat, notice.Id, User.UserId, message.Date, reading, token);
    }
}
