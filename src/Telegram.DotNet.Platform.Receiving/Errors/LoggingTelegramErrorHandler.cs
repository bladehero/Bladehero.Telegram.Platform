using Microsoft.Extensions.Logging;

namespace Telegram.DotNet.Platform.Receiving.Errors;

internal sealed class LoggingTelegramErrorHandler(ILogger<LoggingTelegramErrorHandler> logger) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        logger.LogError(
            telegramError.Exception,
            "Unexpected error occured handled by telegram bot `{BotId}`.",
            telegramError.Client.BotId
        );
        return Task.CompletedTask;
    }
}
