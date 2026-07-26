using Bladehero.Telegram.Platform.Receiving.Commands.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed;

public abstract class TypedTelegramCommand<T> : TypedTelegramCommand
    where T : class
{
    private TypedCommandRequest<T>? _typedCommandRequest;

    public override Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token)
    {
        if (Type != request.Update.Type)
        {
            return Task.FromResult(false);
        }

        var property = UpdateProperties[request.Update.Type];
        var value = property.GetValue(request.Update)!;
        var payload = (T)value;
        _typedCommandRequest = new TypedCommandRequest<T>(request.Update.Id, payload, request.Client);
        return CanHandleAsync(_typedCommandRequest, token);
    }

    public sealed override Task HandleAsync(CommandRequest request, CancellationToken token)
    {
        if (_typedCommandRequest is null)
        {
            throw new InvalidOperationException(
                "Request was not initialized properly through the CanHandleAsync method"
            );
        }

        return HandleAsync(_typedCommandRequest, token);
    }

    protected abstract Task<bool> CanHandleAsync(TypedCommandRequest<T> request, CancellationToken token);

    protected abstract Task HandleAsync(TypedCommandRequest<T> request, CancellationToken token);
}
