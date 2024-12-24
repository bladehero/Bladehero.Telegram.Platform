namespace Telegram.DotNet.Platform.Receiving.Errors;

public interface ITelegramErrorHandler
{
    Task HandleAsync(TelegramError telegramError);
}
