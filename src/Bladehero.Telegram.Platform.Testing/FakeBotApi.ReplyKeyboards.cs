using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Testing;

// Reply keyboards and ForceReply: each chat has one such markup at a time, set by the send that carries it and kept
// off the message itself, which holds inline keyboards only.
public sealed partial class FakeBotApi
{
    private readonly Dictionary<long, ReplyMarkup> _replyMarkups = [];

    // The reply keyboard the user's app shows in the chat, whether it hid it, and the message that set it.
    internal (JsonObject Keyboard, bool Hidden, int MessageId)? ReplyKeyboardFor(long chatId, long userId)
    {
        lock (_gate)
        {
            return _replyMarkups.TryGetValue(chatId, out var markup) && !markup.IsForceReply && markup.AppliesTo(userId)
                ? (markup.Markup.DeepClone().AsObject(), markup.Done.Contains(userId), markup.MessageId)
                : null;
        }
    }

    // After a press: the app hides a one-time keyboard, if it's still the one pressed.
    internal void Pressed(long chatId, long userId, int keyboardMessageId)
    {
        lock (_gate)
        {
            if (
                _replyMarkups.TryGetValue(chatId, out var markup)
                && markup.MessageId == keyboardMessageId
                && markup.Markup["one_time_keyboard"]?.GetValue<bool>() is true
            )
            {
                markup.Done.Add(userId);
            }
        }
    }

    // The message a ForceReply for the user asks them to reply to, now answered; null when there is none.
    internal int? TakeForceReply(long chatId, long userId)
    {
        lock (_gate)
        {
            if (
                !_replyMarkups.TryGetValue(chatId, out var markup)
                || !markup.IsForceReply
                || !markup.AppliesTo(userId)
                || !markup.Done.Add(userId)
            )
            {
                return null;
            }

            return markup.MessageId;
        }
    }

    // Under _gate, after a send: its reply keyboard or ForceReply replaces the chat's, and a removal clears it.
    private JsonObject WithReplyMarkup(ChatHistory chat, JsonObject parameters, JsonObject posted)
    {
        if (parameters["reply_markup"] is not JsonObject markup)
        {
            return posted;
        }

        if (markup["remove_keyboard"]?.GetValue<bool>() is true)
        {
            _replyMarkups.Remove(chat.Id);
        }
        else if (markup["keyboard"] is JsonArray || markup["force_reply"]?.GetValue<bool>() is true)
        {
            var targets = markup["selective"]?.GetValue<bool>() is true ? TargetsOf(posted) : null;
            _replyMarkups[chat.Id] = new ReplyMarkup(
                markup.DeepClone().AsObject(),
                posted["message_id"]!.GetValue<int>(),
                targets
            );
        }

        return posted;
    }

    // Under _gate. Edits take inline keyboards only.
    private static void ThrowIfNotInline(JsonObject parameters)
    {
        if (parameters["reply_markup"] is JsonObject markup && markup["inline_keyboard"] is null)
        {
            throw Refuse(400, "Bad Request: inline keyboard expected");
        }
    }

    // A selective markup's users: those @mentioned by username, and the sender of the message it replies to.
    private HashSet<long> TargetsOf(JsonObject message)
    {
        var text = message["text"]?.GetValue<string>() ?? message["caption"]?.GetValue<string>() ?? "";
        var mentioned = Mention().Matches(text).Select(match => match.Groups[1].Value).ToArray();

        HashSet<long> targets =
        [
            .. _people
                .Values.Where(person =>
                    person["username"]?.GetValue<string>() is { } username
                    && mentioned.Contains(username, StringComparer.OrdinalIgnoreCase)
                )
                .Select(person => person["id"]!.GetValue<long>()),
        ];

        if (message["reply_to_message"]?["from"]?["id"]?.GetValue<long>() is { } repliedTo)
        {
            targets.Add(repliedTo);
        }

        return targets;
    }

    [GeneratedRegex("(?<![A-Za-z0-9_])@([A-Za-z0-9_]{5,32})(?![A-Za-z0-9_])")]
    private static partial Regex Mention();

    private sealed class ReplyMarkup(JsonObject markup, int messageId, HashSet<long>? targets)
    {
        public JsonObject Markup { get; } = markup;

        public int MessageId { get; } = messageId;

        public bool IsForceReply => Markup["force_reply"]?.GetValue<bool>() is true;

        // Users who hid a one-time keyboard, or answered the ForceReply.
        public HashSet<long> Done { get; } = [];

        // Everyone, unless the markup is selective.
        public bool AppliesTo(long userId) => targets?.Contains(userId) ?? true;
    }
}
