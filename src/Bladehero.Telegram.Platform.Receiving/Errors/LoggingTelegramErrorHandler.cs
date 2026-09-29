using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Receiving.Errors;

// Every error is logged: one from an update at Error, a timed-out call included; a failed poll at Warning, as passing
// network trouble that polling waits out. Shutdown's own cancellation never reaches here.
internal sealed class LoggingTelegramErrorHandler(ILogger<LoggingTelegramErrorHandler> logger) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        if (telegramError.Update is { } update)
        {
            logger.LogError(
                telegramError.Exception,
                "Telegram bot {BotId} failed on update {UpdateId}.",
                telegramError.Client.BotId,
                update.Id
            );
        }
        else
        {
            logger.LogWarning(
                telegramError.Exception,
                "Telegram bot {BotId} failed to poll.",
                telegramError.Client.BotId
            );
        }

        return Task.CompletedTask;
    }
}
