using Bladehero.Telegram.Platform.History;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

// Logs written inside carry TelegramUpdateId, and Bot API calls made inside are recorded as caused by the update.
internal static class UpdateLogScope
{
    // Null when there is no update.
    public static IDisposable? Begin(ILogger logger, Update? update) =>
        update is null
            ? null
            : new Scope(
                logger.BeginScope(new Dictionary<string, object> { ["TelegramUpdateId"] = update.Id }),
                TelegramHistoryCause.Begin(update)
            );

    private sealed class Scope(IDisposable? log, IDisposable cause) : IDisposable
    {
        public void Dispose()
        {
            cause.Dispose();
            log?.Dispose();
        }
    }
}
