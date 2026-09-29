using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Photos;

// Downloads a photo to measure it, then sends it back by its file id: Telegram already has it, so nothing is uploaded.
internal sealed class PhotoEchoCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Photo is { Length: > 0 });

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        // Telegram sends a photo in several sizes, the largest last.
        var largest = message.Photo![^1];

        using var content = new MemoryStream();
        await client.GetInfoAndDownloadFile(largest.FileId, content, token);

        await client.SendPhoto(
            message.Chat,
            InputFile.FromFileId(largest.FileId),
            caption: $"Nice photo! ({content.Length} bytes)",
            cancellationToken: token
        );
    }
}
