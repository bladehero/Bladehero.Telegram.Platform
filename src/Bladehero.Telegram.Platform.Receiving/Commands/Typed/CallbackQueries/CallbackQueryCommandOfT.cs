using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>A button command whose callback data is parsed once into <see cref="Parsed"/>.</summary>
/// <remarks>
/// A <see cref="ButtonAttribute"/> <typeparamref name="TData"/> decodes without any code; for other data, override
/// <see cref="Parse"/>, where a tuple such as <c>(string Action, Guid Id)</c> carries several fields. A tap is then
/// parsed, <see cref="CheckAsync"/> takes, declines or rejects it, and <c>HandleAsync</c> runs for a taken one or
/// <see cref="RejectedAsync"/> for a rejected one.
/// </remarks>
/// <typeparam name="TData">The data, such as a <see cref="ButtonAttribute"/> record struct.</typeparam>
public abstract class CallbackQueryCommand<TData> : CallbackQueryCommand
    where TData : struct
{
    private ButtonCheck _check;

    /// <summary>
    /// The value <see cref="Parse"/> returned for the tapped button, set once it accepted the data, before
    /// <see cref="CheckAsync"/> runs.
    /// </summary>
    protected TData Parsed { get; private set; }

    /// <inheritdoc/>
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
        if (!await ResolveAsync(request, token))
        {
            return false;
        }

        _check = await CheckAsync(request, token);
        return !_check.IsDeclined;
    }

    /// <summary>
    /// The value behind <paramref name="data"/>, or <c>null</c> if the button is another command's. By default it
    /// decodes a <see cref="ButtonAttribute"/> <typeparamref name="TData"/> with
    /// <see cref="ButtonData.TryDecode{TButton}"/>.
    /// </summary>
    /// <remarks>
    /// Override it for data of another kind, or to accept an older format for a while as well:
    /// <c>base.Parse(data) ?? Legacy(data)</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Not overridden, and <typeparamref name="TData"/> isn't button data; startup refuses such a command first.
    /// </exception>
    protected virtual TData? Parse(string data) => ButtonData.TryDecode<TData>(data, out var button) ? button : null;

    /// <summary>
    /// Whether the command takes the tap <see cref="Parse"/> accepted: <see cref="ButtonCheck.Accept"/> (the default),
    /// <see cref="ButtonCheck.Decline"/> for another command's, or <see cref="ButtonCheck.Reject"/> to refuse it, e.g.
    /// a tap on someone else's card.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="Parse"/> and, for known users, once <c>User</c> is resolved. It runs alongside other
    /// commands' checks, before any of them handles the update, so keep it free of side effects.
    /// </remarks>
    protected virtual Task<ButtonCheck> CheckAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => Task.FromResult(ButtonCheck.Accept);

    /// <summary>
    /// Runs instead of <c>HandleAsync</c> for a tap <see cref="CheckAsync"/> rejected. By default it answers with the
    /// check's <see cref="ButtonCheck.Answer"/> and <see cref="ButtonCheck.ShowAlert"/>, silently without an answer,
    /// and ignores Telegram's "query is too old" for a tap answered too late.
    /// </summary>
    /// <remarks>Override it to edit or delete the card as well; then answer the query yourself.</remarks>
    protected virtual async Task RejectedAsync(
        TypedCommandRequest<CallbackQuery> request,
        ButtonCheck check,
        CancellationToken token
    )
    {
        try
        {
            await request.Client.AnswerCallbackQuery(
                request.Payload.Id,
                check.Answer,
                check.ShowAlert,
                cancellationToken: token
            );
        }
        catch (ApiRequestException error) when (error.Message.Contains("query is too old"))
        {
            // The user no longer waits for this answer.
        }
    }

    // Between Parse and CheckAsync; known-user commands resolve the user here.
    private protected virtual Task<bool> ResolveAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => Task.FromResult(true);

    private protected sealed override Task HandleTypedAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => _check.IsRejected ? RejectedAsync(request, _check, token) : HandleAsync(request, token);
}
