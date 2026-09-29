using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// An album prompt's button, "album:{ownerId}:{groupId}".
[Button("album")]
internal readonly record struct ReadAlbum(long OwnerId, string GroupId);

// Reads every page of the album at once, and shows the receipt on the prompt.
internal sealed class ReadAlbumButton(ReceiptAlbums albums, IReceiptReader reader, ReceiptCard card)
    : KnownUserCallbackQueryCommand<Member, ReadAlbum>
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
        var prompt = query.Message!;
        var (ownerId, groupId) = Parsed;

        // Read already, by a tap just before, or lost to a restart. The prompt may be a receipt card by now, so its
        // buttons stay.
        if (albums.Take(new AlbumKey(prompt.Chat.Id, ownerId, groupId)) is not { } album)
        {
            await client.AnswerCallbackQuery(query.Id, "This receipt is no longer pending.", cancellationToken: token);
            return;
        }

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(prompt.Chat, prompt.Id, "Reading your receipt…", cancellationToken: token);

        if (await ReceiptFiles.DownloadAsync(client, album.Files, token) is not { } pages)
        {
            await client.EditMessageText(prompt.Chat, prompt.Id, ReceiptFiles.TooBig, cancellationToken: token);
            return;
        }

        var reading = await reader.ReadAsync(pages, album.Caption, token);
        await card.ShowAsync(client, prompt.Chat, prompt.Id, User.UserId, album.SentAt, reading, token);
    }
}
