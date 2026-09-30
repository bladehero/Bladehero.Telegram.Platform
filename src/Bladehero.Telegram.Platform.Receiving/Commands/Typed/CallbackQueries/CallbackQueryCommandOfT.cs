using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>A button command whose callback data is parsed once into <see cref="Parsed"/>.</summary>
/// <remarks>
/// A tap is parsed, then checked with <see cref="CheckAsync"/>; an accepted tap runs <c>HandleAsync</c>, a rejected one
/// <see cref="RejectedAsync"/>.
/// </remarks>
/// <typeparam name="TData">A <see cref="ButtonAttribute"/> struct, or any struct <see cref="Parse"/> reads.</typeparam>
public abstract class CallbackQueryCommand<TData> : CallbackQueryCommand
    where TData : struct
{
    private ButtonCheck _check;

    /// <summary>The value <see cref="Parse"/> returned for the tapped button.</summary>
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
    /// The value behind <paramref name="data"/>, or <c>null</c> for another command's button; by default it decodes a
    /// <see cref="ButtonAttribute"/> <typeparamref name="TData"/>.
    /// </summary>
    /// <remarks>
    /// Override it for other data, or to also accept an older format: <c>base.Parse(data) ?? Legacy(data)</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Not overridden, and <typeparamref name="TData"/> can't be button data.
    /// </exception>
    protected virtual TData? Parse(string data) => ButtonData.TryDecode<TData>(data, out var button) ? button : null;

    /// <summary>
    /// Takes, declines or rejects a parsed tap; <see cref="ButtonCheck.Accept"/> by default.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="Parse"/> and, for known users, after <c>User</c> is resolved. It runs alongside other
    /// commands' checks, so keep it free of side effects.
    /// </remarks>
    protected virtual Task<ButtonCheck> CheckAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) => Task.FromResult(ButtonCheck.Accept);

    /// <summary>Answers a rejected tap with the check's answer, in place of <c>HandleAsync</c>.</summary>
    /// <remarks>Override it to edit or delete the card as well, and answer the query yourself.</remarks>
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
