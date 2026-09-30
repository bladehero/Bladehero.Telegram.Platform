using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// An in-memory Telegram Bot API. <see cref="CreateClient"/> returns a real <see cref="TelegramBotClient"/>, so
/// requests are serialized as in production but never leave the process.
/// </summary>
/// <remarks>
/// Calls are recorded in <see cref="Calls"/> (all but <c>getUpdates</c> and file downloads) and answered as Telegram
/// would, its errors and limits included; the bot can write only to a chat a test user opened or an update brought. A
/// method the fake does not support fails with an error naming it. <see cref="Fail"/> makes Telegram refuse a call,
/// and <see cref="FailNetwork"/> makes it never arrive. One host at a time: dispose a host before starting another on
/// the same fake, as for a restart, since the fake doesn't refuse a second poller as Telegram does.
/// </remarks>
public sealed partial class FakeBotApi
{
    internal const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    private const long BotId = 1234567;
    private const long FirstPersonId = 7_000_000_001;
    private const long FirstGroupId = -1000000000001;
    private const string DefaultScope = """{"type":"default"}""";
    private const string FilePathPrefix = $"/file/bot{Token}/";
    private const int DownloadLimit = 20 * 1024 * 1024;

    private static readonly HashSet<string> ChatActions =
    [
        "typing",
        "upload_photo",
        "record_video",
        "upload_video",
        "record_voice",
        "upload_voice",
        "upload_document",
        "choose_sticker",
        "find_location",
        "record_video_note",
        "upload_video_note",
    ];

    // Methods that take no chat_id, so a failure for one chat could never apply.
    private static readonly HashSet<string> MethodsWithoutAChat = new(StringComparer.OrdinalIgnoreCase)
    {
        "getMe",
        "getFile",
        "answerCallbackQuery",
        "setWebhook",
        "getWebhookInfo",
        "deleteWebhook",
        "getMyCommands",
        "setMyCommands",
    };

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
    private long _lastMediaGroupId;

    /// <summary>Every Bot API call the bot made, oldest first; copies, so changing one changes no record.</summary>
    public IReadOnlyList<BotApiCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls.Select(call => call with { Parameters = call.Parameters.DeepClone().AsObject() })];
            }
        }
    }

    /// <summary>
    /// The Telegram user id of the test user named <paramref name="firstName"/>, which is also their private chat's
    /// id: reserved now for a new name, and the one <c>PrivateChat</c> and <c>Member</c> use later.
    /// </summary>
    /// <remarks>
    /// For seeding an app's users before the host starts. The bot can write to the user only once the test opens
    /// their chat with <c>PrivateChat</c>, so open it before the bot writes first. Ids are Telegram-sized, from
    /// 7 000 000 001, so data carrying them is as long as in production.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="firstName"/> is blank.</exception>
    public long UserIdOf(string firstName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        return Person(firstName)["id"]!.GetValue<long>();
    }

    /// <summary>A real bot client whose requests this fake answers.</summary>
    public ITelegramBotClient CreateClient() =>
        new TelegramBotClient(new TelegramBotClientOptions(Token), new HttpClient(new Transport(this)));

    /// <summary>The command menu the bot set for <paramref name="scope"/>; the default scope when null.</summary>
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
    /// Makes Telegram refuse <paramref name="method"/> (e.g. <c>sendMessage</c>) with <paramref name="error"/>: every
    /// call, or only the next <paramref name="times"/>; with <paramref name="chatId"/>, only the calls to that chat,
    /// e.g. a user who blocked the bot. A refused call is recorded but changes nothing.
    /// </summary>
    /// <remarks>
    /// A method's first matching failure applies until its <paramref name="times"/> run out, then the next one does.
    /// To fail startup calls, arrange this before passing the fake to <see cref="TelegramTestHost"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The method is <c>getUpdates</c>, which the test host owns, or has no chat while <paramref name="chatId"/> is
    /// given.
    /// </exception>
    public void Fail(string method, BotApiError error, int? times = null, long? chatId = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        AddFailure(method, error, times, chatId);
    }

    /// <summary>
    /// Makes the network fail for <paramref name="method"/>: the call never reaches Telegram, and the bot's client
    /// throws a <c>RequestException</c> over an <see cref="HttpRequestException"/>. The call is recorded all the same.
    /// </summary>
    /// <remarks>
    /// Every call, or only the next <paramref name="times"/>; with <paramref name="chatId"/>, only the calls to that
    /// chat. It takes turns with <see cref="Fail"/>'s failures by the same rules.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The method is <c>getUpdates</c>, which the test host owns, or has no chat while <paramref name="chatId"/> is
    /// given.
    /// </exception>
    public void FailNetwork(string method, int? times = null, long? chatId = null) =>
        AddFailure(method, error: null, times, chatId);

    // A null error fails the network instead.
    private void AddFailure(string method, BotApiError? error, int? times, long? chatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

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

        if (chatId is not null && MethodsWithoutAChat.Contains(method))
        {
            throw new ArgumentException(
                $"{method} has no chat, so it cannot fail for one; leave chatId out to fail every call.",
                nameof(chatId)
            );
        }

        lock (_gate)
        {
            _failures.Add(new Failure(method, error, times, chatId));
        }
    }

    internal int Enqueue(JsonObject update)
    {
        ExpectAnswerTo(update);
        return _updates.Add(update);
    }

    // A query id that comes again, as in a raw update, can be answered again.
    private void ExpectAnswerTo(JsonObject update)
    {
        if (update["callback_query"]?["id"]?.GetValue<string>() is { } queryId)
        {
            lock (_gate)
            {
                _callbackAnswers[queryId] = null;
            }
        }
    }

    internal JsonObject? CallbackAnswer(string queryId)
    {
        lock (_gate)
        {
            return _callbackAnswers.GetValueOrDefault(queryId)?.DeepClone().AsObject();
        }
    }

    internal Task HandledAsync(int updateId) => _updates.HandledAsync(updateId);

    internal int Polls => _updates.Polls;

    internal Task PolledAsync(int polls) => _updates.PolledAsync(polls);

    internal (bool Fetched, bool BusyBefore) Progress(int updateId) => _updates.Progress(updateId);

    internal int PollsInFlight => _updates.InFlight;

    // The same first name is the same user in every chat.
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

    // As in Telegram, a private chat's id is the user's id.
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

    // A chat an update brings becomes known, as Telegram lets the bot answer where it heard from.
    internal void KnowChatsIn(JsonObject update)
    {
        var payload = update.FirstOrDefault(field => field.Key != "update_id").Value;
        JsonNode?[] chats = [payload?["chat"], payload?["message"]?["chat"]];

        lock (_gate)
        {
            foreach (var chat in chats.OfType<JsonObject>())
            {
                if (NumberOf(chat["id"]) is { } id)
                {
                    _chats.TryAdd(id, new ChatHistory(chat.DeepClone().AsObject()));
                }
            }
        }
    }

    internal JsonObject Receive(long chatId, JsonObject from, string text)
    {
        var content = new JsonObject { ["text"] = text };

        if (BotCommandEntities(text) is { } entities)
        {
            content["entities"] = entities;
        }

        return Receive(chatId, from, content);
    }

    // The bot_command entity Telegram adds over a leading /command, as far as the characters a command allows go.
    internal static JsonArray? BotCommandEntities(string text) =>
        LeadingBotCommand().Match(text) is { Success: true } command
            ? new JsonArray(
                new JsonObject
                {
                    ["type"] = "bot_command",
                    ["offset"] = 0,
                    ["length"] = command.Length,
                }
            )
            : null;

    [GeneratedRegex("^/[A-Za-z0-9_]{1,32}(@[A-Za-z0-9_]{3,32})?")]
    private static partial Regex LeadingBotCommand();

    internal JsonObject Receive(long chatId, JsonObject from, JsonObject content)
    {
        lock (_gate)
        {
            return _chats[chatId].Post(from, content).DeepClone().AsObject();
        }
    }

    // A user's edit of their own text message: the new text, its command entity recomputed, and edit_date.
    internal JsonObject EditByUser(long chatId, int messageId, string text)
    {
        lock (_gate)
        {
            var chat = _chats[chatId];
            var message =
                chat.Find(messageId) ?? throw new InvalidOperationException("The message is no longer in the chat.");

            message["text"] = text;
            SetOrRemove(message, "entities", BotCommandEntities(text));
            message["edit_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            chat.Changed();
            return message.DeepClone().AsObject();
        }
    }

    // The chat's messages now, and a task that completes on its next change.
    internal (IReadOnlyList<JsonObject> Messages, Task NextChange) MessagesAndNextChange(long chatId)
    {
        lock (_gate)
        {
            var chat = _chats[chatId];
            return ([.. chat.Messages.Select(message => message.DeepClone().AsObject())], chat.NextChange);
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

    internal string NextMediaGroupId() =>
        Interlocked.Increment(ref _lastMediaGroupId).ToString(CultureInfo.InvariantCulture);

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
            "sendChatAction" => SendChatAction(parameters),
            "answerCallbackQuery" => AnswerCallbackQuery(parameters),
            "setWebhook" => SetWebhook(parameters),
            "getWebhookInfo" => WebhookInfo(),
            "deleteWebhook" => DeleteWebhook(),
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

    // Telegram keeps one menu per scope and language.
    private static string MenuKey(JsonNode? scope, JsonNode? languageCode) =>
        $"{scope?.ToJsonString() ?? DefaultScope}|{languageCode?.GetValue<string>()}";

    // Telegram accepts one answer per query it sent.
    private JsonNode AnswerCallbackQuery(JsonObject parameters)
    {
        var queryId = parameters["callback_query_id"]?.GetValue<string>();
        if (queryId is null || !_callbackAnswers.TryGetValue(queryId, out var answer) || answer is not null)
        {
            throw Refuse(400, "Bad Request: query is too old and response timeout expired or query ID is invalid");
        }

        ThrowIfLongerThan(AnswerTextLimit, parameters["text"]?.GetValue<string>(), "Bad Request: MESSAGE_TOO_LONG");

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
        var chat = ChatOf(parameters);
        var (text, entities) = Trimmed(parameters["text"]?.GetValue<string>(), parameters["entities"]);
        if (text is null)
        {
            throw Refuse(400, "Bad Request: message text is empty");
        }

        ThrowIfLongerThan(TextLimit, text, "Bad Request: message is too long");

        var content = new JsonObject { ["text"] = text };

        if (entities is not null)
        {
            content["entities"] = entities;
        }

        if (InlineKeyboardOf(parameters) is { } keyboard)
        {
            content["reply_markup"] = keyboard;
        }

        return chat.Post(Bot(), content).DeepClone().AsObject();
    }

    // Edits the text, the caption, or (field null) only the keyboard. As in Telegram, entities go with their text, and
    // a keyboard or caption the edit leaves out is removed.
    private JsonObject Edit(JsonObject parameters, string? field)
    {
        var chat = ChatOf(parameters);
        var message = chat.Find(MessageIdOf(parameters)) ?? throw Refuse(400, "Bad Request: message to edit not found");

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

        var entitiesField = field == "caption" ? "caption_entities" : "entities";
        var (newValue, newEntities) = field is null
            ? (null, null)
            : Trimmed(parameters[field]?.GetValue<string>(), parameters[entitiesField]);

        switch (field)
        {
            case "text" when newValue is null:
                throw Refuse(400, "Bad Request: message text is empty");
            case "text":
                ThrowIfLongerThan(TextLimit, newValue, "Bad Request: MESSAGE_TOO_LONG");
                break;
            case "caption":
                ThrowIfLongerThan(CaptionLimit, newValue, "Bad Request: MESSAGE_CAPTION_TOO_LONG");
                break;
        }

        var newMarkup = InlineKeyboardOf(parameters);
        if (
            (
                field is null
                || newValue == message[field]?.GetValue<string>()
                    && JsonNode.DeepEquals(newEntities, message[entitiesField])
            ) && JsonNode.DeepEquals(newMarkup, message["reply_markup"])
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
            SetOrRemove(message, entitiesField, newEntities);
        }

        SetOrRemove(message, "reply_markup", newMarkup);
        message["edit_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        chat.Changed();

        return message.DeepClone().AsObject();
    }

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

    private JsonNode Delete(JsonObject parameters)
    {
        if (!ChatOf(parameters).Remove(MessageIdOf(parameters)))
        {
            throw Refuse(400, "Bad Request: message to delete not found");
        }

        return true;
    }

    // "typing…" and the like show for a moment and leave nothing in the chat.
    private JsonNode SendChatAction(JsonObject parameters)
    {
        ChatOf(parameters);

        if (parameters["action"]?.GetValue<string>() is not { } action || !ChatActions.Contains(action))
        {
            throw Refuse(400, "Bad Request: wrong parameter action in request");
        }

        return true;
    }

    // As in Telegram, the bot can only write to a chat it knows: one a user opened or an update brought.
    private ChatHistory ChatOf(JsonObject parameters)
    {
        var chatId = NumberOf(parameters["chat_id"]);
        if (chatId is { } id && _chats.TryGetValue(id, out var chat))
        {
            return chat;
        }

        throw _people.Values.Any(person => person["id"]!.GetValue<long>() == chatId)
            ? Refuse(403, "Forbidden: bot can't initiate conversation with a user")
            : Refuse(400, "Bad Request: chat not found");
    }

    private static int MessageIdOf(JsonObject parameters) =>
        NumberOf(parameters["message_id"]) is { } messageId and <= int.MaxValue
            ? (int)messageId
            : throw Refuse(400, "Bad Request: message identifier is not specified");

    // Uploads send numbers as form text.
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
            if (HasWebhook)
            {
                // Paused like a real round trip, so a loop that cannot clear the webhook does not spin.
                Interlocked.Increment(ref _refusedPolls);
                await Task.Delay(ConflictPause, token);
                return Respond(
                    new BotApiError(
                        409,
                        "Conflict: can't use getUpdates method while webhook is active; use deleteWebhook to delete the webhook first"
                    )
                );
            }

            // Before the poll counts as listening, so a delivery checks the list this poll asked for.
            UpdateAllowedUpdates(parameters);
            var updates = await _updates.TakeAsync(parameters, token);
            return Respond(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = updates });
        }

        JsonNode result;
        lock (_gate)
        {
            _calls.Add(new BotApiCall(method, parameters.DeepClone().AsObject()));

            switch (TakeFailure(method, parameters))
            {
                case { Error: { } error }:
                    return Respond(error);
                case not null:
                    throw new HttpRequestException(
                        $"The network failed for {method}, as FakeBotApi.FailNetwork asked; Telegram never saw it."
                    );
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

    private Failure? TakeFailure(string method, JsonObject parameters)
    {
        var chatId = NumberOf(parameters["chat_id"]);
        var failure = _failures.FirstOrDefault(x => x.Matches(method, chatId));
        if (failure is null)
        {
            return null;
        }

        failure.Happen();
        if (failure.Exhausted)
        {
            _failures.Remove(failure);
        }

        return failure;
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

    // chatId: only calls to that chat, whether chat_id came as a number or as form text. No error: the network fails.
    private sealed class Failure(string method, BotApiError? error, int? times, long? chatId)
    {
        public BotApiError? Error { get; } = error;

        public bool Exhausted => times is 0;

        public bool Matches(string requested, long? requestedChatId) =>
            requested.Equals(method, StringComparison.OrdinalIgnoreCase)
            && (chatId is null || chatId == requestedChatId);

        public void Happen()
        {
            if (times is not null)
            {
                times--;
            }
        }
    }
}
