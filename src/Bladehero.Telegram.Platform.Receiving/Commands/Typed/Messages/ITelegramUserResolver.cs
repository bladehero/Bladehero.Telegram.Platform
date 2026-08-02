namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>
/// Maps a Telegram chat onto whatever the application calls a user.
/// </summary>
/// <remarks>
/// Keyed on the chat id rather than the message so the same resolver serves callback queries and any
/// other update carrying a chat.
/// </remarks>
public interface ITelegramUserResolver<TUser>
    where TUser : class
{
    Task<TUser?> ResolveAsync(long chatId, CancellationToken token);
}
