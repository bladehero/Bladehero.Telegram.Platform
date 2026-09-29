namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>Maps a Telegram chat id to the application's user, for messages and buttons alike.</summary>
public interface ITelegramUserResolver<TUser>
    where TUser : class
{
    Task<TUser?> ResolveAsync(long chatId, CancellationToken token);
}
