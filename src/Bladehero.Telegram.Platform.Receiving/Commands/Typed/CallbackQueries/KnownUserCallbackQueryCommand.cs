using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>A typed button command that runs only for known users, exposing the resolved user.</summary>
/// <remarks>
/// Data is parsed before the user is resolved, so another command's button costs no lookup. An unresolved or missing
/// chat declines the update.
/// </remarks>
public abstract class KnownUserCallbackQueryCommand<TUser, TData> : CallbackQueryCommand<TData>
    where TUser : class
    where TData : struct
{
    internal ITelegramUserResolver<TUser> UserResolver { get; init; } = null!;

    protected TUser User { get; private set; } = null!;

    private protected sealed override async Task<bool> AcceptsAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    )
    {
        if (request.Payload.Message?.Chat is not { } chat)
        {
            return false;
        }

        var user = await UserResolver.ResolveAsync(chat.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return true;
    }
}
