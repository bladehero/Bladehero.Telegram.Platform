using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>A message command that runs only for known users, exposing the resolved user.</summary>
/// <remarks>
/// An unresolved sender declines the update, so strangers are ignored rather than raising errors; so does a message
/// with no sender, such as one sent on behalf of a channel, without a lookup.
/// </remarks>
public abstract class KnownUserCommand<TUser> : MessageCommand
    where TUser : class
{
    internal ITelegramUserResolver<TUser> UserResolver { get; init; } = null!;

    protected TUser User { get; private set; } = null!;

    protected sealed override async Task<bool> CanHandleAsync(
        TypedCommandRequest<Message> request,
        CancellationToken token
    )
    {
        if (!Matches(request.Payload) || request.Payload.From is not { } sender)
        {
            return false;
        }

        var user = await UserResolver.ResolveAsync(request.Payload.Chat.Id, sender.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return true;
    }

    protected abstract bool Matches(Message message);
}
