using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>A button command whose callback data is parsed once into <see cref="Parsed"/>.</summary>
/// <remarks>
/// <see cref="Parse"/> returns <c>null</c> for another command's data; a tuple such as <c>(string Action, Guid Id)</c>
/// carries several fields.
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

    /// <summary>The value behind <paramref name="data"/>, or <c>null</c> if the button is another command's.</summary>
    protected abstract TData? Parse(string data);

    private protected virtual Task<bool> AcceptsAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => Task.FromResult(true);
}
