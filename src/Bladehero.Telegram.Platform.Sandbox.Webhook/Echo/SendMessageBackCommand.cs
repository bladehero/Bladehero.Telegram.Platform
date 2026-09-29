using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Echo;

// Echoes plain text; bot commands and photos have commands of their own.
internal sealed class SendMessageBackCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text is { } text && !text.StartsWith('/'));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        return client.SendMessage(
            message.Chat,
            $"Reply: {message.Text}",
            replyMarkup: EchoKeyboard.At(loudness: 0),
            cancellationToken: token
        );
    }
}
