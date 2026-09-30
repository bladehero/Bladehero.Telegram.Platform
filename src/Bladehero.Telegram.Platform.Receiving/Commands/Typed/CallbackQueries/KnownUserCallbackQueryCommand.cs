using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>A typed button command that runs only for known users, exposing the resolved user.</summary>
/// <remarks>
/// A tap is parsed, then its user resolved from the button's chat and the tapper, then checked with <c>CheckAsync</c>;
/// an unresolved user or a missing chat declines it.
/// </remarks>
public abstract class KnownUserCallbackQueryCommand<TUser, TData> : CallbackQueryCommand<TData>
    where TUser : class
    where TData : struct
{
    internal ITelegramUserResolver<TUser> UserResolver { get; init; } = null!;

    /// <summary>The user the resolver returned for the tapper, set before <c>CheckAsync</c> runs.</summary>
    protected TUser User { get; private set; } = null!;

    private protected sealed override async Task<bool> ResolveAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    )
    {
        if (request.Payload.Message?.Chat is not { } chat)
        {
            return false;
        }

        var user = await UserResolver.ResolveAsync(chat.Id, request.Payload.From.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return true;
    }
}
