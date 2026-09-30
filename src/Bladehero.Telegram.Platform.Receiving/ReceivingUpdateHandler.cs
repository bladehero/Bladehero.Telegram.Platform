using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

internal sealed class ReceivingUpdateHandler(
    ITelegramCommandExecutor telegramCommandExecutor,
    ITelegramErrorHandler telegramErrorHandler,
    Conversation conversation,
    ITelegramBotIdentity identity,
    ILogger<ReceivingUpdateHandler> logger
) : IUpdateHandler
{
    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken
    )
    {
        using var scope = UpdateLogScope.Begin(logger, update);

        // IsCommand then refuses commands addressed to another bot; while the username is unknown, it accepts them.
        var me =
            identity.Current ?? (identity is TelegramBotIdentity own ? await own.TryGetAsync(cancellationToken) : null);
        BotUsername.Current.Value = me?.Username;

        conversation.Bind(update);
        var request = new CommandRequest(update, botClient);
        await telegramCommandExecutor.ExecuteAsync(request, cancellationToken);
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
