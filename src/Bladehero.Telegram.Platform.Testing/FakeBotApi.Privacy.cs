using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

namespace Bladehero.Telegram.Platform.Testing;

// BotFather's group privacy, and the bot's admin rights in groups: which group messages reach the bot, and whose
// messages it may delete.
public sealed partial class FakeBotApi
{
    private const string OwnUsername = "test_bot";

    // The group's owner, who promotes and demotes the bot; not a test user, so user ids stay as they are.
    private static readonly JsonObject GroupOwner = new()
    {
        ["id"] = FirstPersonId - 1,
        ["is_bot"] = false,
        ["first_name"] = "Owner",
    };

    // Groups where the bot is an admin, with whether it may delete messages there.
    private readonly Dictionary<long, bool> _admin = [];

    // Group messages posted without reaching the bot, whose edits don't reach it either.
    private readonly HashSet<(long ChatId, int MessageId)> _unheard = [];

    /// <summary>
    /// BotFather's group privacy, on by default: a non-admin bot in a group gets only commands, replies to it and
    /// mentions.
    /// </summary>
    public bool PrivacyMode { get; set; } = true;

    internal bool IsAdmin(long chatId)
    {
        lock (_gate)
        {
            return _admin.ContainsKey(chatId);
        }
    }

    // Sets the bot's rights in the group, and returns the my_chat_member update it makes.
    internal JsonObject SetAdmin(long chatId, bool admin, bool canDeleteMessages)
    {
        lock (_gate)
        {
            var wasAdmin = _admin.TryGetValue(chatId, out var couldDelete);
            if (admin)
            {
                _admin[chatId] = canDeleteMessages;
            }
            else
            {
                _admin.Remove(chatId);
            }

            return new JsonObject
            {
                ["my_chat_member"] = new JsonObject
                {
                    ["chat"] = _chats[chatId].Chat,
                    ["from"] = GroupOwner.DeepClone(),
                    ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["old_chat_member"] = wasAdmin ? BotAsAdmin(couldDelete) : BotAsMember("member"),
                    ["new_chat_member"] = admin ? BotAsAdmin(canDeleteMessages) : BotAsMember("member"),
                },
            };
        }
    }

    // Whether Telegram sends the bot this message: in a group with privacy on and the bot not an admin, only a
    // command for it, a reply to it or a mention of it.
    internal bool WouldDeliver(long chatId, JsonObject message)
    {
        lock (_gate)
        {
            if (
                !PrivacyMode
                || _admin.ContainsKey(chatId)
                || !_chats.TryGetValue(chatId, out var chat)
                || !chat.IsGroup
            )
            {
                return true;
            }
        }

        var text = message["text"]?.GetValue<string>() ?? message["caption"]?.GetValue<string>();

        return message["reply_to_message"]?["from"]?["id"]?.GetValue<long>() == BotId
            || text is not null && (IsCommandForTheBot(text) || MentionOfTheBot().IsMatch(text));
    }

    internal void MarkUnheard(long chatId, int messageId)
    {
        lock (_gate)
        {
            _unheard.Add((chatId, messageId));
        }
    }

    internal bool WasHeard(long chatId, int messageId)
    {
        lock (_gate)
        {
            return !_unheard.Contains((chatId, messageId));
        }
    }

    // Under _gate. In a group, only an admin allowed to delete messages may delete someone else's.
    private void ThrowIfCannotDelete(ChatHistory chat, JsonObject message)
    {
        if (chat.IsGroup && message["from"]?["id"]?.GetValue<long>() != BotId && !_admin.GetValueOrDefault(chat.Id))
        {
            throw Refuse(400, "Bad Request: message can't be deleted");
        }
    }

    // Under _gate. Deletes the messages found, skipping unknown ids; if any found can't be deleted, deletes none.
    private JsonNode DeleteMessages(JsonObject parameters)
    {
        var chat = ChatOf(parameters);
        if (parameters["message_ids"] is not JsonArray ids)
        {
            throw Refuse(400, "Bad Request: message identifiers are not specified");
        }

        if (ids.Count > 100)
        {
            throw Refuse(400, "Bad Request: too many message identifiers specified");
        }

        var messageIds = ids.Select(NumberOf).ToArray();
        if (messageIds.Any(id => id is null or <= 0))
        {
            throw Refuse(400, "Bad Request: invalid message identifier specified");
        }

        var found = messageIds.Select(id => chat.Find((int)id!.Value)).OfType<JsonObject>().ToArray();
        foreach (var message in found)
        {
            ThrowIfCannotDelete(chat, message);
        }

        foreach (var message in found)
        {
            chat.Remove(message["message_id"]!.GetValue<int>());
        }

        return true;
    }

    private static bool IsCommandForTheBot(string text)
    {
        if (BotCommands.LengthAtStart(text) is not { } length)
        {
            return false;
        }

        var at = text.IndexOf('@', 0, length);
        return at < 0 || text[(at + 1)..length].Equals(OwnUsername, StringComparison.OrdinalIgnoreCase);
    }

    // Every field the Bot API requires of a ChatMemberAdministrator.
    private static JsonObject BotAsAdmin(bool canDeleteMessages) =>
        new()
        {
            ["status"] = "administrator",
            ["user"] = Bot(),
            ["can_be_edited"] = false,
            ["is_anonymous"] = false,
            ["can_manage_chat"] = true,
            ["can_delete_messages"] = canDeleteMessages,
            ["can_manage_video_chats"] = false,
            ["can_restrict_members"] = false,
            ["can_promote_members"] = false,
            ["can_change_info"] = false,
            ["can_invite_users"] = false,
            ["can_post_stories"] = false,
            ["can_edit_stories"] = false,
            ["can_delete_stories"] = false,
        };

    [GeneratedRegex($"(?<![A-Za-z0-9_])@{OwnUsername}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
    private static partial Regex MentionOfTheBot();
}
