using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

// Answers the tap and leaves the message alone, as the tap may come from a stale view of it.
internal sealed class DefaultButtonRefusalHandler(ILogger<DefaultButtonRefusalHandler> logger) : IButtonRefusalHandler
{
    internal const string NoLongerActiveText = "That button is no longer active.";
    internal const string NotYoursText = "That button isn't yours.";

    public async Task HandleAsync(ButtonRefusal refusal, CancellationToken token)
    {
        var answered = await refusal.AnswerAsync(
            refusal.Reason switch
            {
                ButtonRefusalReason.NoLongerActive => NoLongerActiveText,
                ButtonRefusalReason.NotYours => NotYoursText,
                _ => null,
            },
            token: token
        );

        if (!answered)
        {
            logger.LogDebug("A tap on a {Button} button was answered too late", refusal.ButtonType.Name);
        }
    }
}
