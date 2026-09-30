using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Reactions, one per reactor under each message: the bot's, set with setMessageReaction, and users'. Kept here, not in
// the message.
public sealed partial class FakeBotApi
{
    // ReactionTypeEmoji's emoji, as core.telegram.org/bots/api#reactiontypeemoji lists them.
    private static readonly HashSet<string> ReactionEmoji =
    [
        "\u2764", // ❤
        "\U0001F44D", // 👍
        "\U0001F44E", // 👎
        "\U0001F525", // 🔥
        "\U0001F970", // 🥰
        "\U0001F44F", // 👏
        "\U0001F601", // 😁
        "\U0001F914", // 🤔
        "\U0001F92F", // 🤯
        "\U0001F631", // 😱
        "\U0001F92C", // 🤬
        "\U0001F622", // 😢
        "\U0001F389", // 🎉
        "\U0001F929", // 🤩
        "\U0001F92E", // 🤮
        "\U0001F4A9", // 💩
        "\U0001F64F", // 🙏
        "\U0001F44C", // 👌
        "\U0001F54A", // 🕊
        "\U0001F921", // 🤡
        "\U0001F971", // 🥱
        "\U0001F974", // 🥴
        "\U0001F60D", // 😍
        "\U0001F433", // 🐳
        "\u2764\u200D\U0001F525", // ❤‍🔥
        "\U0001F31A", // 🌚
        "\U0001F32D", // 🌭
        "\U0001F4AF", // 💯
        "\U0001F923", // 🤣
        "\u26A1", // ⚡
        "\U0001F34C", // 🍌
        "\U0001F3C6", // 🏆
        "\U0001F494", // 💔
        "\U0001F928", // 🤨
        "\U0001F610", // 😐
        "\U0001F353", // 🍓
        "\U0001F37E", // 🍾
        "\U0001F48B", // 💋
        "\U0001F595", // 🖕
        "\U0001F608", // 😈
        "\U0001F634", // 😴
        "\U0001F62D", // 😭
        "\U0001F913", // 🤓
        "\U0001F47B", // 👻
        "\U0001F468\u200D\U0001F4BB", // 👨‍💻
        "\U0001F440", // 👀
        "\U0001F383", // 🎃
        "\U0001F648", // 🙈
        "\U0001F607", // 😇
        "\U0001F628", // 😨
        "\U0001F91D", // 🤝
        "\u270D", // ✍
        "\U0001F917", // 🤗
        "\U0001FAE1", // 🫡
        "\U0001F385", // 🎅
        "\U0001F384", // 🎄
        "\u2603", // ☃
        "\U0001F485", // 💅
        "\U0001F92A", // 🤪
        "\U0001F5FF", // 🗿
        "\U0001F192", // 🆒
        "\U0001F498", // 💘
        "\U0001F649", // 🙉
        "\U0001F984", // 🦄
        "\U0001F618", // 😘
        "\U0001F48A", // 💊
        "\U0001F64A", // 🙊
        "\U0001F60E", // 😎
        "\U0001F47E", // 👾
        "\U0001F937\u200D\u2642", // 🤷‍♂
        "\U0001F937", // 🤷
        "\U0001F937\u200D\u2640", // 🤷‍♀
        "\U0001F621", // 😡
    ];

    private readonly Dictionary<(long ChatId, int MessageId), List<(long ReactorId, string Emoji)>> _reactions = [];

    internal static bool IsReaction(string emoji) => ReactionEmoji.Contains(emoji);

    // The reactions under a message, one per reactor, oldest first.
    internal IReadOnlyList<string> ReactionsOn(long chatId, int messageId)
    {
        lock (_gate)
        {
            return _reactions.TryGetValue((chatId, messageId), out var reactions)
                ? [.. reactions.Select(reaction => reaction.Emoji)]
                : [];
        }
    }

    // Sets the user's reaction, or takes it back with null; returns the message_reaction update it makes.
    internal JsonObject UserReacts(long chatId, int messageId, JsonObject person, string? emoji)
    {
        lock (_gate)
        {
            var old = React(chatId, messageId, person["id"]!.GetValue<long>(), emoji);
            return new JsonObject
            {
                ["message_reaction"] = new JsonObject
                {
                    ["chat"] = _chats[chatId].Chat,
                    ["message_id"] = messageId,
                    ["user"] = person.DeepClone(),
                    ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["old_reaction"] = ReactionTypes(old),
                    ["new_reaction"] = ReactionTypes(emoji),
                },
            };
        }
    }

    // Under _gate. The bot's reaction: one emoji, or none with []; it makes no update.
    private JsonNode SetMessageReaction(JsonObject parameters)
    {
        var chat = ChatOf(parameters);
        var reactions = parameters["reaction"] as JsonArray ?? [];
        if (reactions.Count > 1)
        {
            throw Refuse(400, "Bad Request: REACTIONS_TOO_MANY");
        }

        string? emoji = null;
        if (reactions is [var reaction])
        {
            switch (reaction?["type"]?.GetValue<string>())
            {
                case "emoji":
                    emoji = reaction["emoji"]?.GetValue<string>();
                    if (emoji is null || !IsReaction(emoji))
                    {
                        throw Refuse(400, "Bad Request: REACTION_INVALID");
                    }

                    break;
                case "custom_emoji" or "paid":
                    throw Refuse(404, "Not Found: FakeBotApi does not support custom_emoji and paid reactions yet");
                default:
                    throw Refuse(400, "Bad Request: invalid reaction type specified");
            }
        }

        var messageId = MessageIdOf(parameters);
        if (chat.Find(messageId) is null)
        {
            throw Refuse(400, "Bad Request: message to react not found");
        }

        React(chat.Id, messageId, BotId, emoji);
        return true;
    }

    // Under _gate. Replaces the reactor's reaction; returns the one it had.
    private string? React(long chatId, int messageId, long reactorId, string? emoji)
    {
        if (!_reactions.TryGetValue((chatId, messageId), out var reactions))
        {
            _reactions[(chatId, messageId)] = reactions = [];
        }

        var index = reactions.FindIndex(reaction => reaction.ReactorId == reactorId);
        var old = index < 0 ? null : reactions[index].Emoji;
        if (index >= 0)
        {
            reactions.RemoveAt(index);
        }

        if (emoji is not null)
        {
            reactions.Add((reactorId, emoji));
        }

        return old;
    }

    private static JsonArray ReactionTypes(string? emoji) =>
        emoji is null ? [] : [new JsonObject { ["type"] = "emoji", ["emoji"] = emoji }];
}
