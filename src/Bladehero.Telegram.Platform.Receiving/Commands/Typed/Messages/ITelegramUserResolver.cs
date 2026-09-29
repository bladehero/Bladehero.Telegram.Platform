namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>Maps where an update came from to the application's user, for messages and buttons alike.</summary>
public interface ITelegramUserResolver<TUser>
    where TUser : class
{
    /// <summary>The application's user behind an update, or <c>null</c> to decline the update.</summary>
    /// <param name="chatId">The chat the update came from.</param>
    /// <param name="userId">The Telegram user who sent it; in a private chat, the same id as the chat.</param>
    /// <param name="token">Cancels the lookup.</param>
    /// <remarks>
    /// Resolve by <paramref name="userId"/> to know a person in every chat, or by <paramref name="chatId"/> to know a
    /// chat, such as a household's group.
    /// </remarks>
    Task<TUser?> ResolveAsync(long chatId, long userId, CancellationToken token);
}
