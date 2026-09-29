using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Coffee;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Voice;

// A spoken order, open to anyone: "A large one, please" starts the order as "/coffee large" would.
internal sealed class VoiceOrderCommand(ITranscriber transcriber, CoffeeOrdering ordering) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload is { From: not null, Voice: not null });

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var notice = await client.SendMessage(message.Chat, "Listening…", cancellationToken: token);

        using var audio = new MemoryStream();
        await client.GetInfoAndDownloadFile(message.Voice!.FileId, audio, token);
        var transcription = await transcriber.TranscribeAsync(audio.ToArray(), token);

        if (transcription.Text is not { } text)
        {
            var error = transcription.Error ?? "I couldn't make that out.";
            await client.EditMessageText(message.Chat, notice.Id, error, cancellationToken: token);
            return;
        }

        await client.EditMessageText(message.Chat, notice.Id, $"«{text}»", cancellationToken: token);
        await ordering.StartAsync(client, message.Chat, message.From!.Id, CoffeeFlow.SizeIn(text), token);
    }
}
