namespace Bladehero.Telegram.Platform.Receiving.Errors;

public interface ITelegramErrorHandler
{
    Task HandleAsync(TelegramError telegramError);
}
