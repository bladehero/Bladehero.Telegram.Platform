using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Failures;

// Logs every error, as the default handler does, and apologises in the chat the failed update came from.
internal sealed class ApologizingErrorHandler(ITelegramSender sender, ILogger<ApologizingErrorHandler> logger)
    : ITelegramErrorHandler
{
    public async Task HandleAsync(TelegramError telegramError)
    {
        logger.LogError(telegramError.Exception, "Handling update {UpdateId} failed.", telegramError.Update?.Id);

        if (ChatOf(telegramError.Update) is not { } chat)
        {
            return;
        }

        try
        {
            await sender.SendAsync(chat.Id, "Sorry, something went wrong — please try again.");
        }
        catch (RequestException failure)
        {
            // E.g. the user blocked the bot, which may be what failed in the first place; that error stands.
            logger.LogWarning(failure, "Could not apologise in chat {ChatId}.", chat.Id);
        }
    }

    // None for a failed poll, or an update from no chat.
    private static Chat? ChatOf(Update? update) =>
        update switch
        {
            { Message.Chat: { } chat } => chat,
            { EditedMessage.Chat: { } chat } => chat,
            { CallbackQuery.Message.Chat: { } chat } => chat,
            _ => null,
        };
}
