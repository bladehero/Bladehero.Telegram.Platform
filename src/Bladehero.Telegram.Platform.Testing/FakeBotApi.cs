using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// An in-memory stand-in for the Telegram Bot API. The client from <see cref="CreateClient"/> is a real
/// <see cref="TelegramBotClient"/> that sends real HTTP requests and JSON, so the bot's requests are serialized exactly
/// as in production — they just never leave the process.
/// </summary>
/// <remarks>
/// Every Bot API call is recorded in <see cref="Calls"/> — except <c>getUpdates</c>, the polling loop's own traffic —
/// and answered the way Telegram would, including Telegram's own errors: editing a message that was deleted, one the
/// bot did not send, or one without changing it, answering a button tap twice, or asking for a file over the 20 MB
/// bots may download. Files are kept in memory both ways: what users send is served back through <c>getFile</c> and
/// a download, and what the bot uploads can be read from the chat. A download is not a Bot API call, so it is neither
/// recorded nor can be made to <see cref="Fail"/>. A method the fake does not know yet
/// fails with an error naming it, so an unsupported call fails the test instead of passing silently. To see how the
/// bot copes when Telegram refuses a call, <see cref="Fail"/> it.
/// </remarks>
public sealed partial class FakeBotApi
{
    internal const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    private const long BotId = 1234567;
    private const long FirstPersonId = 1001;
    private const long FirstGroupId = -1000000000001;
    private const string DefaultScope = """{"type":"default"}""";
    private const string FilePathPrefix = $"/file/bot{Token}/";
    private const int DownloadLimit = 20 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly List<BotApiCall> _calls = [];
    private readonly List<Failure> _failures = [];
    private readonly Dictionary<long, ChatHistory> _chats = [];
    private readonly Dictionary<string, JsonObject> _people = [];
    private readonly Dictionary<string, long> _groups = [];
    private readonly Dictionary<string, JsonArray> _commandMenus = [];
    private readonly Dictionary<string, JsonObject?> _callbackAnswers = [];
    private readonly FileStore _files = new();
    private readonly UpdateQueue _updates = new();
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

    /// <summary>
    /// The command menu Telegram shows for <paramref name="scope"/> — the default scope when none is given — as the
    /// bot last set it.
    /// </summary>
    public IReadOnlyList<BotCommand> CommandMenu(BotCommandScope? scope = null, string? languageCode = null)
    {
        var key = MenuKey(
            scope is null ? null : JsonSerializer.SerializeToNode(scope, JsonBotAPI.Options),
            languageCode
        );

        lock (_gate)
        {
            return _commandMenus.TryGetValue(key, out var menu)
                ? menu.Deserialize<BotCommand[]>(JsonBotAPI.Options)!
                : [];
        }
    }

    /// <summary>
    /// Makes Telegram refuse <paramref name="method"/> with <paramref name="error"/> — every call from now on, or only
    /// the next <paramref name="times"/> calls. A refused call is still recorded in <see cref="Calls"/>, and changes
    /// nothing.
    /// </summary>
    /// <param name="method">The Bot API method as Telegram names it, such as <c>sendMessage</c>.</param>
    /// <param name="error">What Telegram answers with; <see cref="BotApiError"/> has the common ones.</param>
    /// <param name="times">How many calls to refuse before answering again, or <c>null</c> to refuse them all.</param>
    /// <remarks>
    /// To fail a call the bot makes as it starts, set the failure up on a fake before handing it to
    /// <see cref="TelegramTestHost"/>.
    /// </remarks>
    public void Fail(string method, BotApiError error, int? times = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(error);

        if (times <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "A failure has to happen at least once.");
        }

        if (method.Equals("getUpdates", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "getUpdates is how the test host delivers updates, so it cannot fail. Fail the calls the bot makes instead.",
                nameof(method)
            );
        }

        lock (_gate)
        {
            _failures.Add(new Failure(method, error, times));
        }
    }

    internal int Enqueue(JsonObject update)
    {
        if (update["callback_query"]?["id"]?.GetValue<string>() is { } queryId)
        {
            lock (_gate)
            {
                _callbackAnswers.TryAdd(queryId, null);
            }
        }

        return _updates.Add(update);
    }

    internal JsonObject? CallbackAnswer(string queryId)
    {
        lock (_gate)
        {
            return _callbackAnswers.GetValueOrDefault(queryId)?.DeepClone().AsObject();
        }
    }

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

        if (BotCommandEntities(text) is { } entities)
        {
            content["entities"] = entities;
        }

        return Receive(chatId, from, content);
    }

    // How Telegram marks a bot command that starts a text or caption, or null when it starts with none.
    internal static JsonArray? BotCommandEntities(string text) =>
        BotCommandLength(text) is > 1 and var length
            ? new JsonArray(
                new JsonObject
                {
                    ["type"] = "bot_command",
                    ["offset"] = 0,
                    ["length"] = length,
                }
            )
            : null;

    internal JsonObject Receive(long chatId, JsonObject from, JsonObject content)
    {
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

    private JsonNode Answer(
        string method,
        JsonObject parameters,
        IReadOnlyDictionary<string, Attachment> attachments
    ) =>
        method switch
        {
            "getMe" => Bot(),
            "sendMessage" => Send(parameters),
            "sendPhoto" => SendFile(parameters, attachments, FileKind.Photo),
            "sendDocument" => SendFile(parameters, attachments, FileKind.Document),
            "sendVoice" => SendFile(parameters, attachments, FileKind.Voice),
            "editMessageText" => Edit(parameters, "text"),
            "editMessageCaption" => Edit(parameters, "caption"),
            "editMessageReplyMarkup" => Edit(parameters, field: null),
            "deleteMessage" => Delete(parameters),
            "answerCallbackQuery" => AnswerCallbackQuery(parameters),
            "getWebhookInfo" => new JsonObject
            {
                ["url"] = "",
                ["has_custom_certificate"] = false,
                ["pending_update_count"] = 0,
            },
            "deleteWebhook" => true,
            "getMyCommands" => GetCommandMenu(parameters),
            "setMyCommands" => SetCommandMenu(parameters),
            "getFile" => GetFile(parameters),
            _ => throw Refuse(404, $"Not Found: FakeBotApi does not answer {method} yet"),
        };

    private JsonNode GetCommandMenu(JsonObject parameters) =>
        _commandMenus.GetValueOrDefault(MenuKey(parameters["scope"], parameters["language_code"]))?.DeepClone()
        ?? new JsonArray();

    private JsonNode SetCommandMenu(JsonObject parameters)
    {
        _commandMenus[MenuKey(parameters["scope"], parameters["language_code"])] =
            parameters["commands"]?.DeepClone().AsArray() ?? [];
        return true;
    }

    // Telegram keeps a menu per scope and language, and a request without a scope means the default one.
    private static string MenuKey(JsonNode? scope, JsonNode? languageCode) =>
        $"{scope?.ToJsonString() ?? DefaultScope}|{languageCode?.GetValue<string>()}";

    // Telegram takes one answer per callback query: a second one, or one to a query it never sent, is refused.
    private JsonNode AnswerCallbackQuery(JsonObject parameters)
    {
        var queryId = parameters["callback_query_id"]?.GetValue<string>();
        if (queryId is null || !_callbackAnswers.TryGetValue(queryId, out var answer) || answer is not null)
        {
            throw Refuse(400, "Bad Request: query is too old and response timeout expired or query ID is invalid");
        }

        _callbackAnswers[queryId] = parameters.DeepClone().AsObject();
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

    // Edits the message's text, its caption, or — when field is null — only its keyboard. Telegram removes the inline
    // keyboard from an edited message unless the edit passes one again, and removes the caption when an edit of it
    // passes none.
    private JsonObject Edit(JsonObject parameters, string? field)
    {
        var message =
            ChatOf(parameters).Find(MessageIdOf(parameters))
            ?? throw Refuse(400, "Bad Request: message to edit not found");

        if (message["from"]?["id"]?.GetValue<long>() != BotId)
        {
            throw Refuse(400, "Bad Request: message can't be edited");
        }

        var carriesFile =
            message.ContainsKey("photo") || message.ContainsKey("voice") || message.ContainsKey("document");
        switch (field)
        {
            case "text" when carriesFile:
                throw Refuse(400, "Bad Request: there is no text in the message to edit");
            case "caption" when !carriesFile:
                throw Refuse(400, "Bad Request: there is no caption in the message to edit");
        }

        var newValue = field is null ? null : NonBlank(parameters[field]?.GetValue<string>());
        if (field == "text" && newValue is null)
        {
            throw Refuse(400, "Bad Request: message text is empty");
        }

        var newMarkup = InlineKeyboardOf(parameters);
        if (
            (field is null || newValue == message[field]?.GetValue<string>())
            && JsonNode.DeepEquals(newMarkup, message["reply_markup"])
        )
        {
            throw Refuse(
                400,
                "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message"
            );
        }

        if (field is not null)
        {
            SetOrRemove(message, field, newValue);
        }

        SetOrRemove(message, "reply_markup", newMarkup);
        message["edit_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return message.DeepClone().AsObject();
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void SetOrRemove(JsonObject message, string field, JsonNode? value)
    {
        if (value is null)
        {
            message.Remove(field);
        }
        else
        {
            message[field] = value;
        }
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
            throw Refuse(400, "Bad Request: message to delete not found");
        }

        return true;
    }

    private ChatHistory ChatOf(JsonObject parameters)
    {
        if (NumberOf(parameters["chat_id"]) is not { } chatId)
        {
            throw Refuse(400, "Bad Request: chat not found");
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
        NumberOf(parameters["message_id"]) is { } messageId and <= int.MaxValue
            ? (int)messageId
            : throw Refuse(400, "Bad Request: message identifier is not specified");

    // A number, whether it came as JSON or as the text of a form field, as the fields of an upload do.
    private static long? NumberOf(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return
            value.TryGetValue<string>(out var text)
            && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.AbsolutePath.StartsWith(FilePathPrefix, StringComparison.Ordinal))
        {
            return Download(request.RequestUri.AbsolutePath[FilePathPrefix.Length..]);
        }

        var method = request.RequestUri.Segments[^1];
        var (parameters, attachments) = await ReadAsync(request.Content, token);

        if (method == "getUpdates")
        {
            var updates = await _updates.TakeAsync(parameters, token);
            return Respond(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = updates });
        }

        JsonNode result;
        lock (_gate)
        {
            _calls.Add(new BotApiCall(method, parameters.DeepClone().AsObject()));

            if (TakeFailure(method) is { } failure)
            {
                return Respond(failure);
            }

            try
            {
                result = Answer(method, parameters, attachments);
            }
            catch (Refusal refusal)
            {
                return Respond(refusal.Error);
            }
        }

        return Respond(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = result });
    }

    private BotApiError? TakeFailure(string method)
    {
        var failure = _failures.FirstOrDefault(x => x.Matches(method));
        if (failure is null)
        {
            return null;
        }

        failure.Happen();
        if (failure.Exhausted)
        {
            _failures.Remove(failure);
        }

        return failure.Error;
    }

    private static HttpResponseMessage Respond(BotApiError error)
    {
        var body = new JsonObject
        {
            ["ok"] = false,
            ["error_code"] = error.ErrorCode,
            ["description"] = error.Description,
        };

        if (error.RetryAfter is { } retryAfter)
        {
            body["parameters"] = new JsonObject { ["retry_after"] = retryAfter };
        }

        return Respond((HttpStatusCode)error.ErrorCode, body);
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

    private static Refusal Refuse(int errorCode, string description) => new(new BotApiError(errorCode, description));

    private sealed class Refusal(BotApiError error) : Exception(error.Description)
    {
        public BotApiError Error { get; } = error;
    }

    private sealed class Failure(string method, BotApiError error, int? times)
    {
        public BotApiError Error { get; } = error;

        public bool Exhausted => times is 0;

        public bool Matches(string requested) => requested.Equals(method, StringComparison.OrdinalIgnoreCase);

        public void Happen()
        {
            if (times is not null)
            {
                times--;
            }
        }
    }
}
