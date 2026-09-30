using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>A message command that runs only for known users, exposing the resolved user.</summary>
/// <remarks>
/// An unresolved sender declines the update, so strangers are ignored rather than raising errors. A message with no
/// sender, or one sent on behalf of a chat (a channel's post in a group, or an anonymous admin), whose sender is a
/// placeholder rather than a person, is declined without a lookup.
/// </remarks>
public abstract class KnownUserCommand<TUser> : MessageCommand
    where TUser : class
{
    internal ITelegramUserResolver<TUser> UserResolver { get; init; } = null!;

    /// <summary>The user the resolver returned for the sender, set before <see cref="AcceptsAsync"/> runs.</summary>
    protected TUser User { get; private set; } = null!;

    /// <inheritdoc/>
    protected sealed override async Task<bool> CanHandleAsync(
        TypedCommandRequest<Message> request,
        CancellationToken token
    )
    {
        if (
            !Matches(request.Payload)
            || request.Payload.From is not { } sender
            || request.Payload.SenderChat is not null
        )
        {
            return false;
        }

        var user = await UserResolver.ResolveAsync(request.Payload.Chat.Id, sender.Id, token);
        if (user is null)
        {
            return false;
        }

        User = user;
        return await AcceptsAsync(request, token);
    }

    /// <summary>
    /// Whether the message is for this command, such as <c>message.IsCommand("/last")</c>; asked before the resolver,
    /// so another command's message costs no lookup.
    /// </summary>
    protected abstract bool Matches(Message message);

    /// <summary>Whether the command takes a known sender's message; <c>true</c> by default.</summary>
    /// <remarks>
    /// Runs once <see cref="User"/> is resolved, alongside other commands' checks, so keep it free of side effects.
    /// </remarks>
    protected virtual Task<bool> AcceptsAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(true);
}
