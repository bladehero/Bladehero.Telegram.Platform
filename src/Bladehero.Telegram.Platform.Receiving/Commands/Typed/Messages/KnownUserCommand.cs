using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>A message command that runs only for known users, exposing the resolved user.</summary>
/// <remarks>An unresolved chat declines the update, so strangers are ignored rather than raising errors.</remarks>
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
        if (!Matches(request.Payload))
        {
            return false;
        }

        var user = await UserResolver.ResolveAsync(request.Payload.Chat.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return true;
    }

    protected abstract bool Matches(Message message);
}
