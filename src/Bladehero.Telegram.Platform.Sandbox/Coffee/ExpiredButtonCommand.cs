using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

internal sealed class ExpiredButtonCommand : CallbackQueryCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Data?.StartsWith(CoffeeFlow.Buttons) is true);

    protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
        request.Client.AnswerCallbackQuery(request.Payload.Id, "This order is closed.", cancellationToken: token);
}
