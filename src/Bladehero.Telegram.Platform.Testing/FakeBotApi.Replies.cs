using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Replies, forwards and copies: a message carries the one it replies to, without that one's own reply.
public sealed partial class FakeBotApi
{
    // What a copy or forward takes over from the original.
    private static readonly string[] ContentFields =
    [
        "text",
        "entities",
        "caption",
        "caption_entities",
        "photo",
        "document",
        "voice",
    ];

    // The message as a reply shows it, or null when it's no longer in the chat.
    internal JsonObject? ReplyTarget(long chatId, int messageId)
    {
        lock (_gate)
        {
            return _chats.TryGetValue(chatId, out var chat) ? AsReplyTarget(chat.Find(messageId)) : null;
        }
    }

    // Under _gate. The message a send replies to, from reply_parameters or the legacy reply_to_message_id; null for
    // none, or when it's gone and the send allows that.
    private static JsonObject? ReplyTargetOf(ChatHistory chat, JsonObject parameters)
    {
        var reply = parameters["reply_parameters"] as JsonObject;
        var messageId = NumberOf(reply?["message_id"] ?? parameters["reply_to_message_id"]);
        if (messageId is null)
        {
            return null;
        }

        if (
            reply?["quote"] is not null
            || NumberOf(reply?["chat_id"]) is { } otherChat && otherChat != chat.Id
            || reply?["chat_id"] is JsonValue name && name.TryGetValue<string>(out var text) && text.StartsWith('@')
        )
        {
            throw Refuse(400, "Bad Request: FakeBotApi does not support replies to another chat or quotes yet");
        }

        if (AsReplyTarget(chat.Find((int)messageId)) is { } target)
        {
            return target;
        }

        return IsTrue(reply?["allow_sending_without_reply"] ?? parameters["allow_sending_without_reply"])
            ? null
            : throw Refuse(400, "Bad Request: message to be replied not found");
    }

    // Under _gate. A copy of another message, from the bot and with a forward_origin naming the original sender.
    private JsonObject Forward(JsonObject parameters)
    {
        ThrowIfProtected("forwardMessage", parameters);
        var chat = ChatOf(parameters);
        var original = SourceOf(parameters) ?? throw Refuse(400, "Bad Request: message to forward not found");

        var content = ContentOf(original);
        content["forward_origin"] =
            original["forward_origin"]?.DeepClone()
            ?? new JsonObject
            {
                ["type"] = "user",
                ["sender_user"] = original["from"]?.DeepClone(),
                ["date"] = original["date"]?.DeepClone(),
            };

        // A keyboard is forwarded only without callback buttons, which would reach the original's bot.
        if (
            original["reply_markup"]?["inline_keyboard"] is JsonArray rows
            && !rows.OfType<JsonArray>().SelectMany(row => row).Any(button => button?["callback_data"] is not null)
        )
        {
            content["reply_markup"] = original["reply_markup"]!.DeepClone();
        }

        return chat.Post(Bot(), content).DeepClone().AsObject();
    }

    // Under _gate. A copy without an origin; its caption and keyboard are the call's when given.
    private JsonObject Copy(JsonObject parameters)
    {
        ThrowIfProtected("copyMessage", parameters);
        var chat = ChatOf(parameters);
        var original = SourceOf(parameters) ?? throw Refuse(400, "Bad Request: message to copy not found");

        var content = ContentOf(original);
        if (parameters.ContainsKey("caption"))
        {
            var (caption, entities) = Trimmed(
                parameters["caption"]?.GetValue<string>(),
                parameters["caption_entities"]
            );
            ThrowIfLongerThan(CaptionLimit, caption, "Bad Request: message caption is too long");
            SetOrRemove(content, "caption", caption);
            SetOrRemove(content, "caption_entities", entities);
        }

        if (InlineKeyboardOf(parameters) is { } keyboard)
        {
            content["reply_markup"] = keyboard;
        }

        if (ReplyTargetOf(chat, parameters) is { } target)
        {
            content["reply_to_message"] = target;
        }

        var copy = WithReplyMarkup(chat, parameters, chat.Post(Bot(), content));
        return new JsonObject { ["message_id"] = copy["message_id"]!.DeepClone() };
    }

    private JsonObject? SourceOf(JsonObject parameters) =>
        NumberOf(parameters["from_chat_id"]) is { } fromChatId
        && _chats.TryGetValue(fromChatId, out var from)
        && NumberOf(parameters["message_id"]) is { } messageId
            ? from.Find((int)messageId)
            : null;

    private static JsonObject ContentOf(JsonObject original)
    {
        var content = new JsonObject();
        foreach (var field in ContentFields)
        {
            if (original[field] is { } value)
            {
                content[field] = value.DeepClone();
            }
        }

        return content;
    }

    private static void ThrowIfProtected(string method, JsonObject parameters)
    {
        if (IsTrue(parameters["protect_content"]))
        {
            throw Refuse(404, $"Not Found: FakeBotApi does not answer {method} with protect_content yet");
        }
    }

    private static JsonObject? AsReplyTarget(JsonObject? message)
    {
        if (message?.DeepClone().AsObject() is not { } target)
        {
            return null;
        }

        target.Remove("reply_to_message");
        return target;
    }

    // A JSON true, or form text "true" in any case.
    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue value
        && (
            value.TryGetValue<bool>(out var flag) && flag
            || value.TryGetValue<string>(out var text) && bool.TryParse(text, out flag) && flag
        );
}
