using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Which updates Telegram sends the bot: the types it asked for (allowed_updates) with getUpdates, or with setWebhook
// while a webhook is set. An omitted list keeps the previous one, and an empty list means Telegram's default.
public sealed partial class FakeBotApi
{
    // Telegram's default leaves these out: a bot gets them only by asking.
    private static readonly string[] SentOnlyWhenAskedFor =
    [
        "chat_member",
        "message_reaction",
        "message_reaction_count",
    ];

    private JsonArray? _pollingAllowedUpdates;

    // The update's type: its one field besides update_id, e.g. message or callback_query.
    internal static string? UpdateTypeOf(JsonObject update) =>
        update.Select(field => field.Key).FirstOrDefault(key => key != "update_id");

    // Throws, before anything changes, when Telegram would not send an update of this type to the bot.
    internal void ThrowIfNotAllowed(string? updateType)
    {
        if (updateType is null)
        {
            return;
        }

        string[] askedFor;
        lock (_gate)
        {
            var list = _webhook is null ? _pollingAllowedUpdates : _webhook["allowed_updates"] as JsonArray;
            askedFor = [.. (list ?? []).Select(type => type!.GetValue<string>())];
        }

        var enumName = string.Concat(updateType.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

        if (askedFor.Length == 0 && SentOnlyWhenAskedFor.Contains(updateType))
        {
            throw new InvalidOperationException(
                $"Telegram sends {updateType} only to a bot that asks for it: add UpdateType.{enumName} to "
                    + "AllowedUpdates."
            );
        }

        if (askedFor.Length > 0 && !askedFor.Contains(updateType))
        {
            throw new InvalidOperationException(
                $"Telegram would not send this {updateType} to the bot: it asked only for "
                    + $"{string.Join(", ", askedFor)} (allowed_updates). Add UpdateType.{enumName} to AllowedUpdates."
            );
        }
    }

    private void RememberPollingAllowedUpdates(JsonObject parameters)
    {
        if (parameters["allowed_updates"] is JsonArray list)
        {
            lock (_gate)
            {
                _pollingAllowedUpdates = list.DeepClone().AsArray();
            }
        }
    }
}
