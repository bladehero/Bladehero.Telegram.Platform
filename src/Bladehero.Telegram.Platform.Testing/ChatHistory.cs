using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// A chat's messages as they now stand, and every state each has had. As in Telegram, message ids count up per chat,
// whoever posts.
internal sealed class ChatHistory(JsonObject chat)
{
    private readonly List<JsonObject> _messages = [];
    private JsonObject _chat = chat;

    // Copies of each message as posted and after every change; kept after a deletion.
    private readonly Dictionary<int, List<JsonObject>> _revisions = [];
    private TaskCompletionSource _changed = NewSignal();
    private int _lastMessageId;

    public long Id { get; } = chat["id"]!.GetValue<long>();

    public IReadOnlyList<JsonObject> Messages => _messages;

    // The chat as messages show it.
    public JsonObject Chat => _chat.DeepClone().AsObject();

    public bool IsGroup => _chat["type"]?.GetValue<string>() is "group" or "supergroup";

    // Completes on the chat's next change: a message posted, edited or deleted.
    public Task NextChange => _changed.Task;

    public JsonObject Post(JsonObject from, JsonObject content)
    {
        var message = new JsonObject
        {
            ["message_id"] = ++_lastMessageId,
            ["date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["chat"] = _chat.DeepClone(),
            ["from"] = from.DeepClone(),
        };

        foreach (var (key, value) in content)
        {
            message[key] = value?.DeepClone();
        }

        _messages.Add(message);
        _revisions[_lastMessageId] = [message.DeepClone().AsObject()];
        Changed();
        return message;
    }

    public JsonObject? Find(int messageId) =>
        _messages.FirstOrDefault(message => message["message_id"]!.GetValue<int>() == messageId);

    public IReadOnlyList<JsonObject> Revisions(int messageId) => _revisions.GetValueOrDefault(messageId) ?? [];

    public bool Remove(int messageId)
    {
        if (Find(messageId) is not { } message || !_messages.Remove(message))
        {
            return false;
        }

        Changed();
        return true;
    }

    // Called after a message found here was edited, next to Changed.
    public void Revise(JsonObject message) =>
        _revisions[message["message_id"]!.GetValue<int>()].Add(message.DeepClone().AsObject());

    // The chat as later messages show it, e.g. with a detail its user was given since.
    public void Describe(JsonObject chat) => _chat = chat;

    // Called after a message found here was edited.
    public void Changed()
    {
        var changed = _changed;
        _changed = NewSignal();
        changed.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
