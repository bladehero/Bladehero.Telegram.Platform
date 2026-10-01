using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A member's file that cannot be a receipt, such as a zip or a spreadsheet.
internal sealed class UnsupportedFileCommand : KnownUserMessageCommand<Member>
{
    protected override bool Matches(Message message) =>
        message.Document is { } document && !ReceiptFiles.IsReadable(document.MimeType);

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(
            request.Payload.Chat,
            "Send a photo or a PDF of the receipt.",
            cancellationToken: token
        );
}
