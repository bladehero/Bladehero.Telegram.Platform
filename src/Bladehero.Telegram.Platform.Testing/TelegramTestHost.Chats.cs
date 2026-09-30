using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Deliveries a chat change makes only when Telegram would send them.
public sealed partial class TelegramTestHost
{
    // Delivers the update, or skips it when the bot doesn't ask for its type; the change it reports stands either way.
    internal async Task DeliverIfAllowedAsync(string updateType, Func<JsonObject> compose, CancellationToken token)
    {
        try
        {
            await DeliverAsync(updateType, compose, token);
        }
        catch (InvalidOperationException) when (!Api.IsAllowed(updateType))
        {
            // Telegram would not send it.
        }
    }
}
