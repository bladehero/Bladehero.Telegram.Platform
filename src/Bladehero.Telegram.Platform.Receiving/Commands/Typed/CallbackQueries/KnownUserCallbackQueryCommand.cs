using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>
/// A button command with typed callback data that only runs for chats the application recognises, exposing the
/// resolved user alongside <see cref="CallbackQueryCommand{TData}.Parsed"/>.
/// </summary>
/// <remarks>
/// The data is parsed before the user is resolved, so a button meant for another command never costs a lookup.
/// An unresolved chat — or a button on an inline-mode message, which has no chat — declines the update rather
/// than throwing.
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
