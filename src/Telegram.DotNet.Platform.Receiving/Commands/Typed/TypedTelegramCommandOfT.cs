using Telegram.DotNet.Platform.Receiving.Commands.Execution;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed;

public abstract class TypedTelegramCommand<T> : TypedTelegramCommand
    where T : class
{
    private TypedCommandRequest<T>? _typedCommandRequest;

    public override Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token)
    {
        var payload = (T)UpdateProperties[request.Update.Type].GetValue(request.Update)!;
        _typedCommandRequest = new TypedCommandRequest<T>(request.Update.Id, payload, request.Client);
        return CanHandleAsync(_typedCommandRequest, token);
    }

    public sealed override Task HandleAsync(CommandRequest request, CancellationToken token = default)
    {
        if (_typedCommandRequest is null)
        {
            throw new InvalidOperationException(
                "Request was not initialized properly through the CanHandleAsync method"
            );
        }

        return HandleAsync(_typedCommandRequest, token);
    }

    protected abstract Task<bool> CanHandleAsync(TypedCommandRequest<T> request, CancellationToken token = default);

    protected abstract Task HandleAsync(TypedCommandRequest<T> request, CancellationToken token = default);
}
