using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.History;

// Turns the bot's traffic into entries, one per event and per message.
internal static class TelegramHistoryEntries
{
    // Request fields that hold secrets, at any depth; never kept.
    private static readonly string[] Secrets = ["secret_token", "provider_token"];

    // Calls whose result is a secret: only their request is kept.
    private static readonly string[] SecretResults = ["getManagedBotToken", "replaceManagedBotToken"];

    // Bot API JSON that keeps non-ASCII text, such as Cyrillic or emoji, readable; quotes and control characters are
    // still escaped.
    private static readonly JsonSerializerOptions KeptJson = new(JsonBotAPI.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Ids come from the result first, then from the request's Bot API fields.
    internal static IReadOnlyList<TelegramHistoryEntry> FromCall(
        IRequest request,
        object? result,
        Exception? error,
        Update? cause,
        DateTimeOffset time,
        bool keepJson
    )
    {
        var fields = JsonSerializer.SerializeToNode(request, request.GetType(), KeptJson) as JsonObject ?? [];
        RemoveSecrets(fields);

        var requestJson = keepJson ? fields.ToJsonString(KeptJson) : null;
        var keepsResult = !SecretResults.Contains(request.MethodName);

        // A get… call's messages are only read, not sent or changed by it.
        var changesMessages = !request.MethodName.StartsWith("get", StringComparison.Ordinal);
        var call = new TelegramHistoryEntry
        {
            Time = time,
            Direction = TelegramHistoryDirection.Outgoing,
            Kind = request.MethodName,
            UpdateId = cause?.Id,
            ChatId = Value<long>(fields["chat_id"]),
            UserId = Value<long>(fields["user_id"]),
            InlineMessageId = Text(fields["inline_message_id"]),
            Text = Text(fields["text"]) ?? Text(fields["caption"]),
            ErrorCode = (error as ApiRequestException)?.ErrorCode,
            Error = error?.Message,
        };

        // The request, and the part of the result the entry is about.
        string? Json(object? part) =>
            requestJson is null ? null
            : part is null || !keepsResult ? $$"""{"request":{{requestJson}}}"""
            : $$"""{"request":{{requestJson}},"result":{{Serialize(part)}}}""";

        IReadOnlyList<TelegramHistoryEntry> entries = result switch
        {
            Message message when changesMessages => [FromMessage(call, message, Json(message))],
            Message[] { Length: > 0 } messages when changesMessages =>
            [
                .. messages.Select(x => FromMessage(call, x, Json(x))),
            ],
            MessageId id when changesMessages => [call with { MessageId = id.Id, Json = Json(id) }],
            MessageId[] { Length: > 0 } ids when changesMessages && call.ChatId is not null =>
            [
                .. ids.Select(x => call with { MessageId = x.Id, Json = Json(x) }),
            ],
            TGFile file => [call with { FileId = file.FileId, Json = Json(file) }],
            SentWebAppMessage sent => [call with { InlineMessageId = sent.InlineMessageId, Json = Json(sent) }],
            SentGuestMessage sent => [call with { InlineMessageId = sent.InlineMessageId, Json = Json(sent) }],
            _ => FromRequest(call with { Json = Json(result) }, fields),
        };

        // A message id means something only within a chat, and 0 is none.
        return [.. entries.Select(x => x with { MessageId = x.ChatId is null ? null : MessageIdOf(x.MessageId) })];
    }

    // Telegram's 0 stands for no message.
    internal static int? MessageIdOf(int? id) => id is 0 ? null : id;

    private static void RemoveSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject fields:
                foreach (var secret in Secrets)
                {
                    fields.Remove(secret);
                }

                foreach (var (_, value) in fields)
                {
                    RemoveSecrets(value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    RemoveSecrets(item);
                }

                break;
        }
    }

    // The file a message carries: a photo's largest size, or the one file of any other kind.
    internal static (string? Id, string? Name) FileOf(Message message) =>
        message switch
        {
            { Photo: [_, ..] photo } => (photo.MaxBy(x => (long)x.Width * x.Height)!.FileId, null),
            { Animation: { } animation } => (animation.FileId, animation.FileName),
            { Document: { } document } => (document.FileId, document.FileName),
            { Audio: { } audio } => (audio.FileId, audio.FileName),
            { Video: { } video } => (video.FileId, video.FileName),
            { Voice: { } voice } => (voice.FileId, null),
            { VideoNote: { } videoNote } => (videoNote.FileId, null),
            { Sticker: { } sticker } => (sticker.FileId, null),
            _ => (null, null),
        };

    private static TelegramHistoryEntry FromMessage(TelegramHistoryEntry call, Message message, string? json)
    {
        var (fileId, fileName) = FileOf(message);
        return call with
        {
            ChatId = message.Chat.Id,
            MessageId = message.Id,
            Text = message.Text ?? message.Caption ?? call.Text,
            FileId = fileId,
            FileName = fileName,
            Json = json,
        };
    }

    // The request's message ids, unless they have no chat to belong to, or belong to the one it forwards or copies from.
    private static IReadOnlyList<TelegramHistoryEntry> FromRequest(TelegramHistoryEntry call, JsonObject fields)
    {
        if (call.ChatId is null || fields.ContainsKey("from_chat_id"))
        {
            return [call];
        }

        if (fields["message_ids"] is JsonArray { Count: > 0 } ids)
        {
            return [.. ids.Select(x => call with { MessageId = Value<int>(x) })];
        }

        return [call with { MessageId = Value<int>(fields["message_id"]) }];
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), KeptJson);

    private static T? Value<T>(JsonNode? node)
        where T : struct => node is JsonValue value && value.TryGetValue(out T number) ? number : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
