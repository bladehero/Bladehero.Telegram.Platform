using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.DotNet.Platform.Receiving.Commands;
using Telegram.DotNet.Platform.Receiving.Commands.Execution;
using Telegram.DotNet.Platform.Receiving.Errors;

namespace Telegram.DotNet.Platform.Receiving;

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

    public Task HandlePollingErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var error = new TelegramError(exception, botClient);
        return telegramErrorHandler.HandleAsync(error);
    }
}
