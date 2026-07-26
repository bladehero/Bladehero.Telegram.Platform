using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

internal sealed class ReceivingUpdateHandler(
    ITelegramCommandExecutor telegramCommandExecutor,
    ITelegramErrorHandler telegramErrorHandler
) : IUpdateHandler
{
    public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var request = new CommandRequest(update, botClient);
        return telegramCommandExecutor.ExecuteAsync(request, cancellationToken);
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken
    )
    {
        var error = new TelegramError(exception, botClient);
        return telegramErrorHandler.HandleAsync(error);
    }
}
