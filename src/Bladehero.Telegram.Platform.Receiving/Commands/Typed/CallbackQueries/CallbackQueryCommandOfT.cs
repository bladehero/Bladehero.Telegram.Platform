using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>
/// A button command that reads its callback data as a typed value, exposed to
/// <see cref="TypedTelegramCommand{T}.HandleAsync(TypedCommandRequest{T}, CancellationToken)"/> as
/// <see cref="Parsed"/>.
/// </summary>
/// <remarks>
/// The data is parsed once, while deciding whether to handle the update, so the handler never re-parses it. A
/// value type keeps "declined" unambiguous: <see cref="Parse"/> returns <c>null</c> for data that is not this
/// command's, and a tuple such as <c>(string Action, Guid Id)</c> carries several fields.
/// </remarks>
public abstract class CallbackQueryCommand<TData> : CallbackQueryCommand
    where TData : struct
{
    protected TData Parsed { get; private set; }

    protected sealed override async Task<bool> CanHandleAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    )
    {
        if (request.Payload.Data is not { } data || Parse(data) is not { } parsed)
        {
            return false;
        }

        Parsed = parsed;
        return await AcceptsAsync(request, token);
    }

    /// <summary>
    /// The typed value behind <paramref name="data"/>, or <c>null</c> when the button belongs to another command.
    /// </summary>
    protected abstract TData? Parse(string data);

    private protected virtual Task<bool> AcceptsAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => Task.FromResult(true);
}
