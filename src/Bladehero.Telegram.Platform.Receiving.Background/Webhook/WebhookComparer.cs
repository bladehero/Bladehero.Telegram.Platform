using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

internal static class WebhookComparingExtensions
{
    internal static bool HasNoChangesBasedOn(this WebhookInfo? current, TelegramWebhookConfiguration desired) =>
        !current.HasChangesBasedOn(desired);

    internal static bool HasChangesBasedOn(this WebhookInfo? current, TelegramWebhookConfiguration desired)
    {
        if (current is null)
        {
            return true;
        }

        if (!string.Equals(current.Url, desired.WebhookUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (current.PendingUpdateCount > 0 && desired.DropPendingUpdates)
        {
            return true;
        }

        var currentAllowed = (current.AllowedUpdates ?? []).ToHashSet();
        var desiredAllowed = (desired.AllowedUpdates ?? []).ToHashSet();
        return !currentAllowed.SetEquals(desiredAllowed);
    }
}
