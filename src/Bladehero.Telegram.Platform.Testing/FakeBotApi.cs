using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// An in-memory stand-in for the Telegram Bot API. The client from <see cref="CreateClient"/> is a real
/// <see cref="TelegramBotClient"/> that sends real HTTP requests and JSON, so the bot's requests are serialized exactly
/// as in production — they just never leave the process.
/// </summary>
/// <remarks>
/// Every request is recorded in <see cref="Calls"/> — except <c>getUpdates</c>, the polling loop's own traffic — and
/// answered the way Telegram would, including Telegram's own errors: editing a message that was deleted, one the bot
/// did not send, or one without changing it. A method the fake does not know yet fails with an error naming it, so an
/// unsupported call fails the test instead of passing silently.
/// </remarks>
public sealed class FakeBotApi
{
    internal const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    private const long BotId = 1234567;
    private const long FirstPersonId = 1001;
    private const long FirstGroupId = -1000000000001;

    private readonly object _gate = new();
    private readonly List<BotApiCall> _calls = [];
    private readonly Dictionary<long, ChatHistory> _chats = [];
    private readonly Dictionary<string, JsonObject> _people = [];
    private readonly Dictionary<string, long> _groups = [];
    private readonly UpdateQueue _updates = new();
    private JsonArray _commandMenu = [];
    private long _lastCallbackQueryId;

    public IReadOnlyList<BotApiCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public ITelegramBotClient CreateClient() =>
        new TelegramBotClient(new TelegramBotClientOptions(Token), new HttpClient(new Transport(this)));

    internal int Enqueue(JsonObject update) => _updates.Add(update);

    internal Task HandledAsync(int updateId) => _updates.HandledAsync(updateId);

    // A person is known by their first name: the same name is the same Telegram user in every chat of the test.
    internal JsonObject Person(string firstName)
    {
        lock (_gate)
        {
            if (!_people.TryGetValue(firstName, out var person))
            {
                person = new JsonObject
                {
                    ["id"] = FirstPersonId + _people.Count,
                    ["is_bot"] = false,
                    ["first_name"] = firstName,
                };
                _people[firstName] = person;
            }

            return person.DeepClone().AsObject();
        }
    }

    // A private chat's id is the id of the person the bot talks to, as in Telegram.
    internal long PrivateChatWith(JsonObject person)
    {
        var id = person["id"]!.GetValue<long>();

        lock (_gate)
        {
            _chats.TryAdd(
                id,
                new ChatHistory(
                    new JsonObject
                    {
                        ["id"] = id,
                        ["type"] = "private",
                        ["first_name"] = person["first_name"]!.DeepClone(),
                    }
                )
            );
        }

        return id;
    }

    internal long Group(string title)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(title, out var id))
            {
                id = FirstGroupId - _groups.Count;
                _groups[title] = id;
                _chats[id] = new ChatHistory(
                    new JsonObject
                    {
                        ["id"] = id,
                        ["type"] = "supergroup",
                        ["title"] = title,
                    }
                );
            }

            return id;
        }
    }

    // Posts what a person typed, marking a leading bot command the way Telegram does.
    internal JsonObject Receive(long chatId, JsonObject from, string text)
    {
        var content = new JsonObject { ["text"] = text };

        if (BotCommandLength(text) is > 1 and var length)
        {
            content["entities"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "bot_command",
                    ["offset"] = 0,
                    ["length"] = length,
                }
            );
        }

        lock (_gate)
        {
            return _chats[chatId].Post(from, content).DeepClone().AsObject();
        }
    }

    internal IReadOnlyList<JsonObject> MessagesIn(long chatId)
    {
        lock (_gate)
        {
            return _chats.TryGetValue(chatId, out var chat)
                ? [.. chat.Messages.Select(message => message.DeepClone().AsObject())]
                : [];
        }
    }

    internal string NextCallbackQueryId() => Interlocked.Increment(ref _lastCallbackQueryId).ToString();

    private static int BotCommandLength(string text) =>
        !text.StartsWith('/') ? 0
        : text.IndexOfAny([' ', '\n']) is var end and >= 0 ? end
        : text.Length;

    private JsonNode Answer(string method, JsonObject parameters) =>
        method switch
        {
            "getMe" => Bot(),
            "sendMessage" => Send(parameters),
            "editMessageText" => Edit(parameters, parameters["text"]?.GetValue<string>()),
            "editMessageReplyMarkup" => Edit(parameters, text: null),
            "deleteMessage" => Delete(parameters),
            "answerCallbackQuery" => true,
            "getWebhookInfo" => new JsonObject
            {
                ["url"] = "",
                ["has_custom_certificate"] = false,
                ["pending_update_count"] = 0,
            },
            "deleteWebhook" => true,
            "getMyCommands" => _commandMenu.DeepClone(),
            "setMyCommands" => SetCommandMenu(parameters),
            _ => throw new BotApiError(404, $"Not Found: FakeBotApi does not answer {method} yet"),
        };

    private JsonNode SetCommandMenu(JsonObject parameters)
    {
        _commandMenu = parameters["commands"]?.DeepClone().AsArray() ?? [];
        return true;
    }

    private static JsonObject Bot() =>
        new()
        {
            ["id"] = BotId,
            ["is_bot"] = true,
            ["first_name"] = "Test Bot",
            ["username"] = "test_bot",
        };

    private JsonObject Send(JsonObject parameters)
    {
        var content = new JsonObject { ["text"] = parameters["text"]?.DeepClone() };

        if (InlineKeyboardOf(parameters) is { } keyboard)
        {
            content["reply_markup"] = keyboard;
        }

        return ChatOf(parameters).Post(Bot(), content).DeepClone().AsObject();
    }

    // Telegram removes the inline keyboard from an edited message unless the edit passes one again.
    private JsonObject Edit(JsonObject parameters, string? text)
    {
        var message =
            ChatOf(parameters).Find(MessageIdOf(parameters))
            ?? throw new BotApiError(400, "Bad Request: message to edit not found");

        if (message["from"]?["id"]?.GetValue<long>() != BotId)
        {
            throw new BotApiError(400, "Bad Request: message can't be edited");
        }

        var newText = text ?? message["text"]?.GetValue<string>();
        var newMarkup = InlineKeyboardOf(parameters);

        if (newText == message["text"]?.GetValue<string>() && JsonNode.DeepEquals(newMarkup, message["reply_markup"]))
        {
            throw new BotApiError(
                400,
                "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message"
            );
        }

        message["text"] = newText;
        message["reply_markup"] = newMarkup;
        message["edit_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (message["reply_markup"] is null)
        {
            message.Remove("reply_markup");
        }

        return message.DeepClone().AsObject();
    }

    // Only an inline keyboard belongs to a message. A reply keyboard, or the order to remove one, changes the user's
    // keyboard instead, so the message Telegram returns never carries it.
    private static JsonNode? InlineKeyboardOf(JsonObject parameters) =>
        parameters["reply_markup"] is JsonObject markup && markup.ContainsKey("inline_keyboard")
            ? markup.DeepClone()
            : null;

    private JsonNode Delete(JsonObject parameters)
    {
        if (!ChatOf(parameters).Remove(MessageIdOf(parameters)))
        {
            throw new BotApiError(400, "Bad Request: message to delete not found");
        }

        return true;
    }

    private ChatHistory ChatOf(JsonObject parameters)
    {
        if (parameters["chat_id"] is not JsonValue value || !value.TryGetValue<long>(out var chatId))
        {
            throw new BotApiError(400, "Bad Request: chat not found");
        }

        if (!_chats.TryGetValue(chatId, out var chat))
        {
            chat = new ChatHistory(
                new JsonObject { ["id"] = chatId, ["type"] = chatId > 0 ? "private" : "supergroup" }
            );
            _chats[chatId] = chat;
        }

        return chat;
    }

    private static int MessageIdOf(JsonObject parameters) =>
        parameters["message_id"] is JsonValue value && value.TryGetValue<int>(out var messageId)
            ? messageId
            : throw new BotApiError(400, "Bad Request: message identifier is not specified");

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
    {
        var method = request.RequestUri!.Segments[^1];
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        var parameters = string.IsNullOrWhiteSpace(body) ? [] : JsonNode.Parse(body)!.AsObject();

        if (method == "getUpdates")
        {
            var updates = await _updates.TakeAsync(parameters, token);
            return Respond(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = updates });
        }

        JsonNode result;
        lock (_gate)
        {
            _calls.Add(new BotApiCall(method, parameters.DeepClone().AsObject()));

            try
            {
                result = Answer(method, parameters);
            }
            catch (BotApiError error)
            {
                return Respond(
                    (HttpStatusCode)error.Code,
                    new JsonObject
                    {
                        ["ok"] = false,
                        ["error_code"] = error.Code,
                        ["description"] = error.Message,
                    }
                );
            }
        }

        return Respond(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = result });
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private sealed class Transport(FakeBotApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => api.HandleAsync(request, cancellationToken);
    }

    private sealed class BotApiError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
