using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

internal sealed class OrderCoffeeCommand(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/coffee"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        await conversation.StartAsync(CoffeeFlow.Name, CoffeeFlow.SizeStep, new CoffeeOrder(), token);
        await client.SendMessage(
            message.Chat,
            "What size?",
            replyMarkup: CoffeeFlow.SizeKeyboard,
            cancellationToken: token
        );
    }
}
