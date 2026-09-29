using Bladehero.Telegram.Platform.Receiving.Commands.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed;

/// <summary>
/// A command for a single update type whose <c>CanHandleAsync</c> and <c>HandleAsync</c> get the payload typed.
/// </summary>
/// <remarks>
/// The typed request built for <see cref="CanHandleAsync(TypedCommandRequest{T}, CancellationToken)"/> is reused by
/// <see cref="HandleAsync(TypedCommandRequest{T}, CancellationToken)"/>, since the command is scoped to the update.
/// </remarks>
/// <typeparam name="T">The payload, such as <c>Message</c> for a message.</typeparam>
public abstract class TypedTelegramCommand<T> : TypedTelegramCommand
    where T : class
{
    private TypedCommandRequest<T>? _typedCommandRequest;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public sealed override Task HandleAsync(CommandRequest request, CancellationToken token)
    {
        if (_typedCommandRequest is null)
        {
            throw new InvalidOperationException(
                "Request was not initialized properly through the CanHandleAsync method"
            );
        }

        return HandleTypedAsync(_typedCommandRequest, token);
    }

    // What handling the typed request means; a button command answers a rejected tap here instead.
    private protected virtual Task HandleTypedAsync(TypedCommandRequest<T> request, CancellationToken token) =>
        HandleAsync(request, token);

    /// <summary>
    /// Whether this command handles the payload, asked only for updates of its <see cref="TypedTelegramCommand.Type"/>;
    /// every command that returns <c>true</c> runs.
    /// </summary>
    protected abstract Task<bool> CanHandleAsync(TypedCommandRequest<T> request, CancellationToken token);

    /// <summary>
    /// Handles the payload once <see cref="CanHandleAsync(TypedCommandRequest{T}, CancellationToken)"/> has returned
    /// <c>true</c>, with the same request.
    /// </summary>
    protected abstract Task HandleAsync(TypedCommandRequest<T> request, CancellationToken token);
}
