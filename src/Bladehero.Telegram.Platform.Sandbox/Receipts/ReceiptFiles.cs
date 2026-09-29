using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// Which messages carry a receipt, and fetching their files within the size the reader takes.
internal static class ReceiptFiles
{
    public const string TooBig = "That file is too big — send a photo of the receipt instead.";

    private const long MaxBytes = 5 * 1024 * 1024;

    // Telegram converts every photo to JPEG.
    private const string PhotoMediaType = "image/jpeg";

    public static bool IsReceipt(Message message) =>
        message.Photo is { Length: > 0 } || IsReadable(message.Document?.MimeType);

    public static bool IsReadable(string? mimeType) =>
        mimeType is "application/pdf" || mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) is true;

    // A photo comes in several sizes, the largest last.
    public static ReceiptFile FileOf(Message message) =>
        message.Photo is [.., var largest]
            ? new ReceiptFile(largest.FileId, PhotoMediaType)
            : new ReceiptFile(message.Document!.FileId, message.Document.MimeType!);

    // The pages, or null when a file is too big to read.
    public static async Task<IReadOnlyList<ReceiptPage>?> DownloadAsync(
        ITelegramBotClient client,
        IReadOnlyList<ReceiptFile> files,
        CancellationToken token
    )
    {
        var pages = new List<ReceiptPage>(files.Count);

        foreach (var file in files)
        {
            TGFile info;
            try
            {
                info = await client.GetFile(file.FileId, token);
            }
            catch (ApiRequestException error) when (error.Message.Contains("file is too big"))
            {
                // Over the 20 MB Telegram lets bots download.
                return null;
            }

            if (info.FileSize > MaxBytes)
            {
                return null;
            }

            using var content = new MemoryStream();
            await client.DownloadFile(info, content, token);
            pages.Add(new ReceiptPage(content.ToArray(), file.MediaType));
        }

        return pages;
    }
}

// A receipt's file on Telegram, not downloaded yet.
internal sealed record ReceiptFile(string FileId, string MediaType);
