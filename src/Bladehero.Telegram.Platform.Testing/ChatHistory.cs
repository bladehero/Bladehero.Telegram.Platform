using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// A chat's messages as they now stand. As in Telegram, message ids count up per chat, whoever posts.
internal sealed class ChatHistory(JsonObject chat)
{
    private readonly List<JsonObject> _messages = [];
    private int _lastMessageId;

    public long Id { get; } = chat["id"]!.GetValue<long>();

    public IReadOnlyList<JsonObject> Messages => _messages;

    public JsonObject Post(JsonObject from, JsonObject content)
    {
        var message = new JsonObject
        {
            ["message_id"] = ++_lastMessageId,
            ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["chat"] = chat.DeepClone(),
            ["from"] = from.DeepClone(),
        };

        foreach (var (key, value) in content)
        {
            message[key] = value?.DeepClone();
        }

        _messages.Add(message);
        return message;
    }

    public JsonObject? Find(int messageId) =>
        _messages.FirstOrDefault(message => message["message_id"]!.GetValue<int>() == messageId);

    public bool Remove(int messageId) => Find(messageId) is { } message && _messages.Remove(message);
}
