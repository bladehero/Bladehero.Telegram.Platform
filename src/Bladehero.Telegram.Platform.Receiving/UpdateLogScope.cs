using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

// Logs written inside it carry the update's id as TelegramUpdateId.
internal static class UpdateLogScope
{
    // Null when there is no update.
    public static IDisposable? Begin(ILogger logger, Update? update) =>
        update is null ? null : logger.BeginScope(new Dictionary<string, object> { ["TelegramUpdateId"] = update.Id });
}
