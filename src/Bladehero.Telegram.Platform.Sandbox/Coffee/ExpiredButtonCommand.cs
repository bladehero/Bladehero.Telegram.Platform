using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Runs only when no step took the tap: the order is over, or the card belongs to someone else. Changes nothing.
internal sealed class ExpiredButtonCommand : CallbackQueryCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Data?.StartsWith($"{CoffeeFlow.Name}:") is true);

    protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var query = request.Payload;
        var answer =
            CoffeeButton.Parse(query.Data) is { } button && button.OwnerId != query.From.Id
                ? "This order isn't yours."
                : "That button is no longer active.";

        return request.Client.AnswerCallbackQuery(query.Id, answer, cancellationToken: token);
    }
}
