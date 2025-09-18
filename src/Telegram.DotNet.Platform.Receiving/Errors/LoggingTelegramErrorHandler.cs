using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace Telegram.DotNet.Platform.Receiving.Errors;

internal sealed class LoggingTelegramErrorHandler(ILogger<LoggingTelegramErrorHandler> logger) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        if (
            telegramError.Exception is RequestException
            && telegramError.Exception.InnerException is TaskCanceledException
        )
        {
            return Task.CompletedTask;
        }

        logger.LogError(
            telegramError.Exception,
            "Unexpected error occured handled by telegram bot `{BotId}`.",
            telegramError.Client.BotId
        );
        return Task.CompletedTask;
    }
}
