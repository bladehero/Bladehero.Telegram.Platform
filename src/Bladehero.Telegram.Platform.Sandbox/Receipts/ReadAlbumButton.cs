using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// An album prompt's "album:{ownerId}:{groupId}" button: reads every page at once, and shows the receipt on the prompt.
internal sealed class ReadAlbumButton(ReceiptAlbums albums, IReceiptReader reader, ReceiptCard card)
    : KnownUserCallbackQueryCommand<Member, (long OwnerId, string GroupId)>
{
    private const string Prefix = "album";

    public static string Data(long ownerId, string groupId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{ownerId}:{groupId}");

    protected override (long OwnerId, string GroupId)? Parse(string data) =>
        data.Split(':') is [Prefix, var owner, var groupId]
        && long.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId)
            ? (ownerId, groupId)
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var prompt = query.Message!;
        var (ownerId, groupId) = Parsed;

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
