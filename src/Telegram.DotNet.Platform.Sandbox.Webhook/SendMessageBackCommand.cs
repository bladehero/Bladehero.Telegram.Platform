using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.DotNet.Platform.Receiving.Commands.Typed;
using Telegram.DotNet.Platform.Receiving.Commands.Typed.Messages;

namespace Telegram.DotNet.Platform.Sandbox.Webhook;

public sealed class SendMessageBackCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(true);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        await client.SendMessage(message.Chat, $"Reply: {message.Text}", cancellationToken: token);
    }
}
