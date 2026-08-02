using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>
/// A message command that only runs for chats the application recognises, exposing the resolved user
/// to <see cref="TypedTelegramCommand{T}.HandleAsync(TypedCommandRequest{T}, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// An unresolved chat makes the command decline the update rather than throw, so a stranger messaging
/// the bot is simply ignored instead of raising an error per message.
/// </remarks>
public abstract class KnownUserCommand<TUser>(ITelegramUserResolver<TUser> users) : MessageCommand
    where TUser : class
{
    protected TUser User { get; private set; } = null!;

    protected sealed override async Task<bool> CanHandleAsync(
        TypedCommandRequest<Message> request,
        CancellationToken token
    )
    {
        if (!Matches(request.Payload))
        {
            return false;
        }

        var user = await users.ResolveAsync(request.Payload.Chat.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return true;
    }

    protected abstract bool Matches(Message message);
}
