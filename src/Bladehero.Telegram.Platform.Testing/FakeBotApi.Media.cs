using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Albums and media edits, of photos and documents.
public sealed partial class FakeBotApi
{
    private const int AlbumLimit = 10;

    private const string NotModified =
        "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message";

    // Under _gate. One message per item, sharing a new media_group_id; a single item is a normal message. Every item
    // is checked before any is stored, and reply_markup is ignored, as by Telegram.
    private JsonArray SendMediaGroup(JsonObject parameters, IReadOnlyDictionary<string, Attachment> attachments)
    {
        var chat = ChatOf(parameters);
        var media =
            parameters["media"] as JsonArray ?? throw Refuse(400, "Bad Request: parameter \"media\" is required");

        switch (media.Count)
        {
            case 0:
                throw Refuse(400, "Bad Request: there are no messages to send");
            case > AlbumLimit:
                throw Refuse(400, "Bad Request: too many messages to send as an album");
        }

        var items = media.Select(item => item as JsonObject ?? []).ToArray();
        var kinds = items.Select(item => AlbumKindOf(item["type"]?.GetValue<string>())).ToArray();
        foreach (var item in items)
        {
            ThrowIfNotAttached(item["media"], attachments);
        }

        if (kinds.Contains(FileKind.Document) && kinds.Any(kind => kind != FileKind.Document))
        {
            throw Refuse(400, "Bad Request: document can't be mixed with other media types");
        }

        var captions = items.Select(CaptionOf).ToArray();
        var target = ReplyTargetOf(chat, parameters);

        // Every file is resolved before the first post, so a refused item posts nothing.
        var contents = items
            .Select((item, index) => MediaContent(kinds[index], item, attachments, captions[index]))
            .ToArray();
        var mediaGroupId = items.Length > 1 ? NextMediaGroupId() : null;
        var sent = new JsonArray();
        foreach (var content in contents)
        {
            if (mediaGroupId is not null)
            {
                content["media_group_id"] = mediaGroupId;
            }

            if (target is not null)
            {
                content["reply_to_message"] = target.DeepClone();
            }

            sent.Add(chat.Post(Bot(), content).DeepClone());
        }

        return sent;
    }

    // Under _gate. Replaces the file of the bot's photo, document or text message; the caption and keyboard are the
    // call's, and left out they're removed.
    private JsonObject EditMedia(JsonObject parameters, IReadOnlyDictionary<string, Attachment> attachments)
    {
        ThrowIfNotInline(parameters);
        var chat = ChatOf(parameters);
        var message = chat.Find(MessageIdOf(parameters)) ?? throw Refuse(400, "Bad Request: message to edit not found");

        if (
            message["from"]?["id"]?.GetValue<long>() != BotId
            || message.ContainsKey("voice")
            || IsUneditable(chat, message)
        )
        {
            throw Refuse(400, "Bad Request: message media can't be edited");
        }

        var media =
            parameters["media"] as JsonObject ?? throw Refuse(400, "Bad Request: parameter \"media\" is required");
        var type = media["type"]?.GetValue<string>();
        var inAlbum = message.ContainsKey("media_group_id");
        if (type is not ("photo" or "document"))
        {
            throw inAlbum
                ? Refuse(400, "Bad Request: message content type can't be used in an album")
                : Refuse(404, $"Not Found: FakeBotApi does not support {type} media yet");
        }

        var kind = type == "photo" ? FileKind.Photo : FileKind.Document;
        if (inAlbum && (kind == FileKind.Photo) != message.ContainsKey("photo"))
        {
            throw Refuse(400, "Bad Request: can't change media type in the album");
        }

        ThrowIfNotAttached(media["media"], attachments);
        var caption = CaptionOf(media);
        var keyboard = InlineKeyboardOf(parameters);

        var current = message["photo"]?.AsArray()[^1]?["file_id"] ?? message["document"]?["file_id"];
        if (
            media["media"]?.GetValue<string>() is { } fileId
            && current?.GetValue<string>() == fileId
            && message.ContainsKey(type)
            && caption.Value == message["caption"]?.GetValue<string>()
            && JsonNode.DeepEquals(caption.Entities, message["caption_entities"])
            && JsonNode.DeepEquals(keyboard, message["reply_markup"])
        )
        {
            throw Refuse(400, NotModified);
        }

        // Built before the message changes, so a refused file leaves it as it was.
        var content = MediaContent(kind, media, attachments, caption);
        foreach (var field in new[] { "text", "entities", "photo", "document", "caption", "caption_entities" })
        {
            message.Remove(field);
        }

        foreach (var (field, value) in content)
        {
            message[field] = value?.DeepClone();
        }

        SetOrRemove(message, "reply_markup", keyboard);
        message["edit_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        chat.Revise(message);
        chat.Changed();

        return message.DeepClone().AsObject();
    }

    private static FileKind AlbumKindOf(string? type) =>
        type switch
        {
            "photo" => FileKind.Photo,
            "document" => FileKind.Document,
            "animation" => throw Refuse(
                400,
                "Bad Request: can't parse InputMedia: type \"animation\" can't be used in sendMediaGroup"
            ),
            "audio" or "video" => throw Refuse(404, "Not Found: FakeBotApi does not support audio and video yet"),
            _ => throw Refuse(400, $"Bad Request: can't parse InputMedia: type \"{type}\" is unsupported"),
        };

    private static void ThrowIfNotAttached(JsonNode? media, IReadOnlyDictionary<string, Attachment> attachments)
    {
        if (
            media?.GetValue<string>() is not { } value
            || value.StartsWith(AttachPrefix, StringComparison.Ordinal)
                && !attachments.ContainsKey(value[AttachPrefix.Length..])
        )
        {
            throw Refuse(400, "Bad Request: can't parse InputMedia: media not found");
        }
    }

    // An item's caption and its entities, trimmed and within the limit; raw, as parse_mode isn't applied.
    private static (string? Value, JsonArray? Entities) CaptionOf(JsonObject item)
    {
        var caption = Trimmed(item["caption"]?.GetValue<string>(), item["caption_entities"]);
        ThrowIfLongerThan(CaptionLimit, caption.Value, "Bad Request: message caption is too long");
        return caption;
    }

    private JsonObject MediaContent(
        FileKind kind,
        JsonObject item,
        IReadOnlyDictionary<string, Attachment> attachments,
        (string? Value, JsonArray? Entities) caption
    )
    {
        var field = kind.ToString().ToLowerInvariant();
        var content = FileFor(kind, new JsonObject { [field] = item["media"]!.DeepClone() }, attachments)
            .ToMessageContent();

        if (caption.Value is not null)
        {
            content["caption"] = caption.Value;
            if (caption.Entities is not null)
            {
                content["caption_entities"] = caption.Entities;
            }
        }

        return content;
    }
}
