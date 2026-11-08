using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// A hub's additions to the Bot API, so an app behind one runs its tests against this fake, and the topics a hub makes.
public sealed partial class FakeBotApi
{
    private readonly List<(long Id, string Name)> _hubPeople = [];
    private readonly Dictionary<long, HashSet<long>> _topics = [];
    private long _lastTopicId = 1000;

    /// <summary>
    /// The bot's Threaded Mode, as <c>getMe</c> says. Off by default. While it's off, <c>createForumTopic</c> gets
    /// "the chat is not a forum".
    /// </summary>
    public bool TopicsEnabled { get; set; }

    /// <summary>The manifest the app last sent with <c>setHubApp</c>, or <c>null</c>.</summary>
    public JsonObject? HubApp { get; private set; }

    /// <summary>Sets who <c>getHubPeople</c> answers with, in place of whoever was there.</summary>
    public void SetHubPeople(params (long Id, string Name)[] people)
    {
        lock (_gate)
        {
            _hubPeople.Clear();
            _hubPeople.AddRange(people);
        }
    }

    /// <summary>Opens a private chat with this id, for code that calls the Bot API without a test host.</summary>
    public long OpenPrivateChat(long id, string firstName) =>
        PrivateChatWith(
            new JsonObject
            {
                ["id"] = id,
                ["is_bot"] = false,
                ["first_name"] = firstName,
            }
        );

    /// <summary>Opens a group, for code that calls the Bot API without a test host, and gives its id.</summary>
    public long OpenGroup(string title) => Group(title);

    /// <summary>The topics open in a chat, by thread id.</summary>
    public IReadOnlyCollection<long> TopicsIn(long chatId)
    {
        lock (_gate)
        {
            return _topics.TryGetValue(chatId, out var topics) ? [.. topics] : [];
        }
    }

    /// <summary>Deletes a topic the way a person does: the bot isn't told, and its next send there is refused.</summary>
    public void DeleteTopicByHand(long chatId, long threadId)
    {
        lock (_gate)
        {
            _topics.GetValueOrDefault(chatId)?.Remove(threadId);
        }
    }

    private JsonNode SetHubApp(JsonObject parameters)
    {
        HubApp = parameters.DeepClone().AsObject();
        return true;
    }

    private JsonArray GetHubPeople() =>
        new([.. _hubPeople.Select(person => (JsonNode)new JsonObject { ["id"] = person.Id, ["name"] = person.Name })]);

    private JsonObject CreateForumTopic(JsonObject parameters)
    {
        var chat = NumberOf(parameters["chat_id"]) ?? throw Refuse(400, "Bad Request: chat not found");
        if (!TopicsEnabled)
        {
            throw Refuse(400, "Bad Request: the chat is not a forum");
        }

        var name = parameters["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Refuse(400, "Bad Request: topic name is empty");
        }

        var thread = ++_lastTopicId;
        if (!_topics.TryGetValue(chat, out var topics))
        {
            _topics[chat] = topics = [];
        }

        topics.Add(thread);
        return new JsonObject
        {
            ["message_thread_id"] = thread,
            ["name"] = name,
            ["icon_color"] = 7322096,
        };
    }

    private JsonNode ChangeForumTopic(JsonObject parameters, bool delete)
    {
        var chat = NumberOf(parameters["chat_id"]) ?? throw Refuse(400, "Bad Request: chat not found");
        if (!TopicsEnabled)
        {
            throw Refuse(400, "Bad Request: the chat is not a forum");
        }

        var thread = NumberOf(parameters["message_thread_id"]);
        if (thread is null || !_topics.TryGetValue(chat, out var topics) || !topics.Contains(thread.Value))
        {
            throw Refuse(400, "Bad Request: message thread not found");
        }

        if (delete)
        {
            topics.Remove(thread.Value);
        }

        return true;
    }

    // With Threaded Mode off the thread is ignored, as this fake always has.
    private void ThrowIfNoSuchThread(JsonObject parameters)
    {
        if (!TopicsEnabled || NumberOf(parameters["message_thread_id"]) is not { } thread)
        {
            return;
        }

        var chat = NumberOf(parameters["chat_id"]);
        if (chat is null || !_topics.TryGetValue(chat.Value, out var topics) || !topics.Contains(thread))
        {
            throw Refuse(400, "Bad Request: message thread not found");
        }
    }

    private static JsonNode InThreadOf(JsonObject parameters, JsonNode answer)
    {
        if (NumberOf(parameters["message_thread_id"]) is not { } thread)
        {
            return answer;
        }

        foreach (var message in answer is JsonArray messages ? [.. messages] : new[] { answer })
        {
            if (message is JsonObject sent && sent.ContainsKey("message_id"))
            {
                sent["message_thread_id"] = thread;
                sent["is_topic_message"] = true;
            }
        }

        return answer;
    }
}
