using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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

    // Request fields that say whom a call is about; a call with none of them is about its update's chat and user.
    private static readonly string[] Targets = ["chat_id", "user_id", "inline_message_id", "business_connection_id"];

    internal static IReadOnlyList<TelegramHistoryEntry> FromUpdate(Update update, DateTimeOffset time, bool keepJson)
    {
        var described = Describe(update);
        var entry = described with
        {
            Time = time,
            MessageId = MessageIdOf(described.MessageId),
            Json = keepJson ? JsonSerializer.Serialize(update, KeptJson) : null,
        };

        return update.DeletedBusinessMessages is { MessageIds: [_, ..] ids }
            ? [.. ids.Select(id => entry with { MessageId = MessageIdOf(id) })]
            : [entry];
    }

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

        var origin = cause is not null && !Targets.Any(fields.ContainsKey) ? Describe(cause) : null;
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
            ChatId = Value<long>(fields["chat_id"]) ?? origin?.ChatId,
            UserId = Value<long>(fields["user_id"]) ?? origin?.UserId,
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
            Message message when changesMessages => [About(call, message) with { Json = Json(message) }],
            Message[] { Length: > 0 } messages when changesMessages =>
            [
                .. messages.Select(x => About(call, x) with { Json = Json(x) }),
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
    private static int? MessageIdOf(int? id) => id is 0 ? null : id;

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

    // The update's kind, ids, text and file, without its time or JSON.
    private static TelegramHistoryEntry Describe(Update update)
    {
        var entry = new TelegramHistoryEntry
        {
            Direction = TelegramHistoryDirection.Incoming,
            Kind = KindOf(update.Type),
            UpdateId = update.Id,
        };

        var message =
            update.Message
            ?? update.EditedMessage
            ?? update.ChannelPost
            ?? update.EditedChannelPost
            ?? update.BusinessMessage
            ?? update.EditedBusinessMessage;
        if (message is not null)
        {
            // A channel post has no sender; an anonymous admin's message is from Telegram's placeholder user.
            return About(entry, message) with { UserId = message.From?.Id };
        }

        if (update.GuestMessage is { } guest)
        {
            // Its chat id may be that of another chat of the bot's, so it's no chat to file it under.
            return About(entry, guest) with { ChatId = null, MessageId = null, UserId = guest.From?.Id };
        }

        return update switch
        {
            { CallbackQuery: { } query } => entry with
            {
                // A tap on an inaccessible message still has the message's chat and id.
                ChatId = query.Message?.Chat.Id,
                UserId = query.From.Id,
                MessageId = query.Message?.Id,
                InlineMessageId = query.InlineMessageId,
                Text = query.Data ?? query.GameShortName,
            },
            { InlineQuery: { } query } => entry with { UserId = query.From.Id, Text = query.Query },
            { ChosenInlineResult: { } chosen } => entry with
            {
                UserId = chosen.From.Id,
                InlineMessageId = chosen.InlineMessageId,
                Text = chosen.Query,
            },
            { Poll: { } poll } => entry with { Text = poll.Question },
            { PollAnswer: { } answer } => entry with { ChatId = answer.VoterChat?.Id, UserId = answer.User?.Id },
            { MyChatMember: { } member } => entry with { ChatId = member.Chat.Id, UserId = member.From.Id },
            { ChatMember: { } member } => entry with { ChatId = member.Chat.Id, UserId = member.From.Id },
            { ChatJoinRequest: { } request } => entry with { ChatId = request.Chat.Id, UserId = request.From.Id },
            { MessageReaction: { } reaction } => entry with
            {
                ChatId = reaction.Chat.Id,
                UserId = reaction.User?.Id,
                MessageId = reaction.MessageId,
            },
            { MessageReactionCount: { } count } => entry with { ChatId = count.Chat.Id, MessageId = count.MessageId },
            { ChatBoost: { } boost } => entry with { ChatId = boost.Chat.Id, UserId = BoosterOf(boost.Boost.Source) },
            { RemovedChatBoost: { } removed } => entry with
            {
                ChatId = removed.Chat.Id,
                UserId = BoosterOf(removed.Source),
            },
            { BusinessConnection: { } connection } => entry with
            {
                ChatId = connection.UserChatId,
                UserId = connection.User.Id,
            },
            { DeletedBusinessMessages: { } deleted } => entry with { ChatId = deleted.Chat.Id },
            { ShippingQuery: { } query } => entry with { UserId = query.From.Id },
            { PreCheckoutQuery: { } query } => entry with { UserId = query.From.Id },
            { PurchasedPaidMedia: { } purchase } => entry with { UserId = purchase.From.Id },
            { Subscription: { } subscription } => entry with { UserId = subscription.User.Id },
            { ManagedBot: { } managed } => entry with { UserId = managed.User.Id },
            _ => entry,
        };
    }

    // The message's chat, id, text as Telegram shows it, and file.
    private static TelegramHistoryEntry About(TelegramHistoryEntry entry, Message message)
    {
        var (fileId, fileName) = FileOf(message);
        return entry with
        {
            ChatId = message.Chat.Id,
            MessageId = message.Id,
            Text = message.Text ?? message.Caption ?? entry.Text,
            FileId = fileId,
            FileName = fileName,
        };
    }

    // The file a message carries: a photo's largest size, or the one file of any other kind.
    private static (string? Id, string? Name) FileOf(Message message) =>
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

    // The user behind a boost, when Telegram names one.
    private static long? BoosterOf(ChatBoostSource source) =>
        source switch
        {
            ChatBoostSourcePremium premium => premium.User.Id,
            ChatBoostSourceGiftCode giftCode => giftCode.User.Id,
            ChatBoostSourceGiveaway giveaway => giveaway.User?.Id,
            _ => null,
        };

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

    // The update type as the Bot API names it, such as callback_query.
    private static string KindOf(UpdateType type) =>
        JsonSerializer.SerializeToElement(type, JsonBotAPI.Options).GetString() ?? type.ToString();

    private static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), KeptJson);

    private static T? Value<T>(JsonNode? node)
        where T : struct => node is JsonValue value && value.TryGetValue(out T number) ? number : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
