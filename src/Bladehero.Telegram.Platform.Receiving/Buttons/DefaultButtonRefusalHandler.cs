using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

// Answers the tap and leaves the message as it is.
internal sealed class DefaultButtonRefusalHandler(ILogger<DefaultButtonRefusalHandler> logger) : IButtonRefusalHandler
{
    internal const string NoLongerActiveText = "That button is no longer active.";

    public async Task HandleAsync(ButtonRefusal refusal, CancellationToken token)
    {
        try
        {
            await refusal.Client.AnswerCallbackQuery(
                refusal.Query.Id,
                refusal.Reason is ButtonRefusalReason.NoLongerActive ? NoLongerActiveText : null,
                cancellationToken: token
            );
        }
        catch (ApiRequestException error) when (error.Message.Contains("query is too old"))
        {
            logger.LogDebug(
                "A tap on a {Button} button was answered too late: {Message}",
                refusal.ButtonType.Name,
                error.Message
            );
        }
    }
}
