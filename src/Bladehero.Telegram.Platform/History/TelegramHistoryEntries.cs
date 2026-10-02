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
    // Request fields that hold secrets; never kept.
    private static readonly string[] Secrets = ["secret_token", "provider_token"];

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
        var fields = JsonSerializer.SerializeToNode(request, request.GetType(), JsonBotAPI.Options) as JsonObject ?? [];
        foreach (var secret in Secrets)
        {
            fields.Remove(secret);
        }

        var requestJson = keepJson ? fields.ToJsonString(JsonBotAPI.Options) : null;
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
            : part is null ? $$"""{"request":{{requestJson}}}"""
            : $$"""{"request":{{requestJson}},"result":{{Serialize(part)}}}""";

        return result switch
        {
            Message message => [FromMessage(call, message, Json(message))],
            Message[] { Length: > 0 } messages => [.. messages.Select(x => FromMessage(call, x, Json(x)))],
            MessageId id => [call with { MessageId = id.Id, Json = Json(id) }],
            MessageId[] { Length: > 0 } ids => [.. ids.Select(x => call with { MessageId = x.Id, Json = Json(x) })],
            TGFile file => [call with { FileId = file.FileId, Json = Json(file) }],
            _ => FromRequest(call with { Json = Json(result) }, fields),
        };
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

    // The request's message ids, unless they belong to the chat it forwards or copies from.
    private static IReadOnlyList<TelegramHistoryEntry> FromRequest(TelegramHistoryEntry call, JsonObject fields)
    {
        if (fields.ContainsKey("from_chat_id"))
        {
            return [call];
        }

        if (fields["message_ids"] is JsonArray { Count: > 0 } ids)
        {
            return [.. ids.Select(x => call with { MessageId = Value<int>(x) })];
        }

        return [call with { MessageId = Value<int>(fields["message_id"]) }];
    }

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), JsonBotAPI.Options);

    private static T? Value<T>(JsonNode? node)
        where T : struct => node is JsonValue value && value.TryGetValue(out T number) ? number : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
