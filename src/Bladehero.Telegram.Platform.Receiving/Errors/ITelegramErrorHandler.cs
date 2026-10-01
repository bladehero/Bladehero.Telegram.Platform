namespace Bladehero.Telegram.Platform.Receiving.Errors;

/// <summary>
/// Gets every error the receiver hits, from a command that throws to a failed poll; the default one logs them.
/// </summary>
/// <remarks>
/// Register your own after the receiving services to replace it, e.g. to report errors or apologize in the chat.
/// Shutdown's own cancellation isn't an error and never reaches it.
/// </remarks>
public interface ITelegramErrorHandler
{
    /// <summary>
    /// Handles <paramref name="telegramError"/>; an exception it throws is logged and goes no further.
    /// </summary>
    Task HandleAsync(TelegramError telegramError);
}
