using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Which updates Telegram sends the bot: as in Telegram, one list per bot (allowed_updates), the types the bot asked for
// last with getUpdates or setWebhook. An omitted list keeps it; an empty one, or one naming no type Telegram knows,
// means Telegram's default. Removing the webhook keeps it too.
public sealed partial class FakeBotApi
{
    private static readonly string[] UpdateTypes =
    [
        "message",
        "edited_message",
        "channel_post",
        "edited_channel_post",
        "business_connection",
        "business_message",
        "edited_business_message",
        "deleted_business_messages",
        "message_reaction",
        "message_reaction_count",
        "inline_query",
        "chosen_inline_result",
        "callback_query",
        "shipping_query",
        "pre_checkout_query",
        "purchased_paid_media",
        "poll",
        "poll_answer",
        "my_chat_member",
        "chat_member",
        "chat_join_request",
        "chat_boost",
        "removed_chat_boost",
    ];

    // Telegram's default leaves these out: a bot gets them only by asking.
    private static readonly string[] SentOnlyWhenAskedFor =
    [
        "chat_member",
        "message_reaction",
        "message_reaction_count",
    ];

    // The types the bot asked for; null for Telegram's default.
    private string[]? _allowedUpdates;

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

        string[]? askedFor;
        lock (_gate)
        {
            askedFor = _allowedUpdates;
        }

        var enumName = string.Concat(updateType.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

        if (askedFor is null && SentOnlyWhenAskedFor.Contains(updateType))
        {
            throw new InvalidOperationException(
                $"Telegram sends {updateType} only to a bot that asks for it: add UpdateType.{enumName} to "
                    + "AllowedUpdates."
            );
        }

        if (askedFor is not null && !askedFor.Contains(updateType))
        {
            throw new InvalidOperationException(
                $"Telegram would not send this {updateType} to the bot: it asked only for "
                    + $"{string.Join(", ", askedFor)} (allowed_updates). Add UpdateType.{enumName} to AllowedUpdates."
            );
        }
    }

    // Called by getUpdates (unless refused) and setWebhook with a URL.
    private void UpdateAllowedUpdates(JsonObject parameters)
    {
        if (parameters["allowed_updates"] is not JsonArray list)
        {
            return;
        }

        string[] known =
        [
            .. list.Select(type => type?.GetValue<string>().ToLowerInvariant())
                .OfType<string>()
                .Where(UpdateTypes.Contains)
                .Distinct(),
        ];

        lock (_gate)
        {
            _allowedUpdates = known.Length == 0 ? null : known;
        }
    }

    // As getWebhookInfo shows it: only a list that differs from Telegram's default.
    private JsonArray? AllowedUpdatesInfo()
    {
        if (_allowedUpdates is not { } askedFor)
        {
            return null;
        }

        return askedFor.Order().SequenceEqual(UpdateTypes.Except(SentOnlyWhenAskedFor).Order())
            ? null
            : new JsonArray([.. askedFor.Select(type => (JsonNode)type)]);
    }
}
