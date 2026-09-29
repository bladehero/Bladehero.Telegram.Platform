using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// "/coffee", or "/coffee large" to skip the size step.
[BotCommand("coffee", "Order a coffee", Order = 1)]
internal sealed class OrderCoffeeCommand(CoffeeOrdering ordering) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.From is not null && request.Payload.IsCommand("/coffee"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var size = CoffeeFlow.ParseSize(message.ArgumentsOf("/coffee"));

        return ordering.StartAsync(client, message.Chat, size, token);
    }
}
