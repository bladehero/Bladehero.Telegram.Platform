using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The bot as a chat member: blocked by a user in their private chat.
public sealed partial class FakeBotApi
{
    // What a user who blocked the bot no longer lets it do in their chat.
    private static readonly HashSet<string> RefusedWhenBlocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "sendMessage",
        "sendPhoto",
        "sendDocument",
        "sendVoice",
        "sendChatAction",
        "sendMediaGroup",
        "forwardMessage",
        "copyMessage",
    };

    // Private chats whose user blocked the bot.
    private readonly HashSet<long> _blocked = [];

    internal bool HasBlocked(long chatId)
    {
        lock (_gate)
        {
            return _blocked.Contains(chatId);
        }
    }

    // Sets the block, and returns the my_chat_member update it makes.
    internal JsonObject Block(JsonObject person, bool blocked)
    {
        var chatId = person["id"]!.GetValue<long>();
        lock (_gate)
        {
            if (blocked)
            {
                _blocked.Add(chatId);
            }
            else
            {
                _blocked.Remove(chatId);
            }
        }

        return new JsonObject
        {
            ["my_chat_member"] = new JsonObject
            {
                ["chat"] = PrivateChatOf(person),
                ["from"] = person.DeepClone(),
                ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["old_chat_member"] = BotAsMember(blocked ? "member" : "kicked"),
                ["new_chat_member"] = BotAsMember(blocked ? "kicked" : "member"),
            },
        };
    }

    // Under _gate.
    private void ThrowIfBlocked(string method, JsonObject parameters)
    {
        if (
            RefusedWhenBlocked.Contains(method)
            && NumberOf(parameters["chat_id"]) is { } chatId
            && _blocked.Contains(chatId)
        )
        {
            throw new Refusal(BotApiError.BotBlocked);
        }
    }

    private static JsonObject BotAsMember(string status) =>
        status == "kicked"
            ? new JsonObject
            {
                ["status"] = status,
                ["user"] = Bot(),
                ["until_date"] = 0,
            }
            : new JsonObject { ["status"] = status, ["user"] = Bot() };
}
