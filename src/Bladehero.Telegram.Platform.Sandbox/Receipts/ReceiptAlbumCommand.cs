using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// The pages of a multi-page receipt, sent as an album: the first announces it, and each later one updates the count.
internal sealed class ReceiptAlbumCommand(ReceiptAlbums albums) : KnownUserCommand<Member>
{
    protected override bool Matches(Message message) =>
        message.MediaGroupId is not null && ReceiptFiles.IsReceipt(message);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var groupId = message.MediaGroupId!;
        var album = albums.Open(new AlbumKey(message.Chat.Id, User.UserId, groupId), message.Date);

        var pages = album.Add(ReceiptFiles.FileOf(message), message.Caption);
        var text = pages == 1 ? "Got 1 page." : $"Got {pages} pages.";
        var read = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData(
                pages == 1 ? "Read 1 page" : $"Read {pages} pages",
                ReadAlbumButton.Data(User.UserId, groupId)
            )
        );

        if (album.PromptId is { } promptId)
        {
            await client.EditMessageText(message.Chat, promptId, text, replyMarkup: read, cancellationToken: token);
            return;
        }

        var prompt = await client.SendMessage(message.Chat, text, replyMarkup: read, cancellationToken: token);
        album.PromptId = prompt.Id;
    }
}
