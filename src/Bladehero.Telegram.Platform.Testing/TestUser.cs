using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A person in a <see cref="TestChat"/> who types, sends files and taps buttons. Each action returns once the bot has
/// handled it, rethrowing what a command threw.
/// </summary>
/// <remarks>
/// An action Telegram would not send to the bot, as the bot left its type out of <c>allowed_updates</c>, fails with an
/// <see cref="InvalidOperationException"/> before anything changes.
/// </remarks>
public sealed partial class TestUser
{
    private readonly TelegramTestHost _host;
    private readonly JsonObject _opened;

    internal TestUser(TelegramTestHost host, JsonObject person, TestChat chat)
    {
        _host = host;
        _opened = person;
        Chat = chat;
    }

    /// <summary>The Telegram user id, the same in every chat.</summary>
    public long Id => _opened["id"]!.GetValue<long>();

    /// <summary>The name the user was opened with, which is one user in every chat.</summary>
    public string FirstName => _opened["first_name"]!.GetValue<string>();

    /// <summary>The last name, if the user was given one.</summary>
    public string? LastName => Person["last_name"]?.GetValue<string>();

    /// <summary>The username without @, if the user was given one.</summary>
    public string? Username => Person["username"]?.GetValue<string>();

    /// <summary>The app's language, if the user was given one.</summary>
    public string? LanguageCode => Person["language_code"]?.GetValue<string>();

    /// <summary>The chat the user acts in: their private chat, or the group they are a member of.</summary>
    public TestChat Chat { get; }

    // The user as Telegram now shows them, with details given since this TestUser was opened.
    private JsonObject Person => _host.Api.Person(FirstName);

    /// <inheritdoc cref="TestChat.Messages"/>
    public IReadOnlyList<TestMessage> Messages => Chat.Messages;

    /// <inheritdoc cref="TestChat.LastMessage"/>
    public TestMessage LastMessage => Chat.LastMessage;

    /// <inheritdoc cref="TestChat.LastReply"/>
    public TestMessage LastReply => Chat.LastReply;

    /// <inheritdoc cref="TestChat.Current"/>
    public TestMessage? Current(TestMessage message) => Chat.Current(message);

    /// <inheritdoc cref="TestChat.RevisionsOf"/>
    public IReadOnlyList<TestMessage> RevisionsOf(TestMessage message) => Chat.RevisionsOf(message);

    /// <inheritdoc cref="TestChat.WaitForMessageAsync"/>
    public Task<TestMessage> WaitForMessageAsync(
        Func<TestMessage, bool> match,
        TestMessage? after = null,
        TimeSpan? timeout = null,
        CancellationToken token = default
    ) => Chat.WaitForMessageAsync(match, after, timeout, token);

    /// <summary>Sends <paramref name="text"/>, trimmed as Telegram does.</summary>
    /// <returns>The message as posted: a snapshot that stays valid even if the bot then deletes it.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is blank, or longer than the 4096 characters of a Telegram message.
    /// </exception>
    /// <remarks>While a ForceReply asks the user to reply, it's sent as that reply, as the app opens the field so.</remarks>
    public Task<TestMessage> SendsAsync(string text, CancellationToken token = default)
    {
        text = CheckedText(text);
        ThrowIfBlocked();

        return SendsTextAsync(text, _host.Api.TakeForceReply(Chat.Id, Id), token);
    }

    /// <summary>
    /// Sends a photo in two sizes, smallest first, as Telegram does; the largest downloads as <paramref name="photo"/>.
    /// Any bytes will do; the reported dimensions are nominal.
    /// </summary>
    /// <returns>The message as posted: a snapshot that stays valid even if the bot then deletes it.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="photo"/> is empty, or <paramref name="caption"/> longer than the 1024 characters of a caption.
    /// </exception>
    public Task<TestMessage> SendsPhotoAsync(byte[] photo, string? caption = null, CancellationToken token = default)
    {
        ThrowIfEmpty(photo);

        return SendsFileAsync(PhotoOf(photo), CheckedCaption(caption), token);
    }

    /// <summary>Sends a voice message of <paramref name="duration"/> (one second by default).</summary>
    /// <returns>The message as posted: a snapshot that stays valid even if the bot then deletes it.</returns>
    /// <exception cref="ArgumentException"><paramref name="voice"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public Task<TestMessage> SendsVoiceAsync(byte[] voice, TimeSpan? duration = null, CancellationToken token = default)
    {
        ThrowIfEmpty(voice);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration ?? TimeSpan.Zero, TimeSpan.Zero, nameof(duration));

        return SendsFileAsync(() => _host.Api.UserVoice(voice, duration), caption: null, token);
    }

    /// <summary>
    /// Sends <paramref name="document"/> named <paramref name="fileName"/>. The MIME type comes from the extension
    /// (pdf, txt, csv, json, xml, zip, jpg/jpeg, png, webp, gif, heic, ogg/oga, mp3, mp4, docx, xlsx; otherwise
    /// octet-stream) unless <paramref name="mimeType"/> is given.
    /// </summary>
    /// <returns>The message as posted: a snapshot that stays valid even if the bot then deletes it.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/> is empty, <paramref name="fileName"/> blank, or <paramref name="caption"/> longer
    /// than the 1024 characters of a caption.
    /// </exception>
    public Task<TestMessage> SendsDocumentAsync(
        byte[] document,
        string fileName,
        string? caption = null,
        string? mimeType = null,
        CancellationToken token = default
    )
    {
        ThrowIfEmpty(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return SendsFileAsync(DocumentOf(document, fileName, mimeType), CheckedCaption(caption), token);
    }

    /// <summary>
    /// Sends <paramref name="photos"/> as an album: each its own photo message, in two sizes as
    /// <see cref="SendsPhotoAsync"/> sends one, all in one media group, and each delivered as its own update, in
    /// order, once the bot has handled the one before.
    /// </summary>
    /// <param name="photos">2 to 10 photos.</param>
    /// <param name="caption">Shown under the first photo.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The messages as posted, in order.</returns>
    /// <exception cref="ArgumentException">
    /// Not 2 to 10 photos, an empty one, or <paramref name="caption"/> longer than the 1024 characters of a caption.
    /// </exception>
    /// <remarks>When the bot fails on an item, the rest are not sent.</remarks>
    public Task<IReadOnlyList<TestMessage>> SendsAlbumAsync(
        IReadOnlyList<byte[]> photos,
        string? caption = null,
        CancellationToken token = default
    )
    {
        ThrowIfNotAnAlbum(photos);
        foreach (var photo in photos)
        {
            ThrowIfEmpty(photo, nameof(photos));
        }

        return SendsAlbumAsync([.. photos.Select(PhotoOf)], CheckedCaption(caption), captionOn: 0, token);
    }

    /// <summary>
    /// Sends <paramref name="documents"/> as an album: each its own document message, its MIME type given or from its
    /// file name, all in one media group, and each delivered as its own update, in order, once the bot has handled the
    /// one before.
    /// </summary>
    /// <param name="documents">2 to 10 documents.</param>
    /// <param name="caption">Shown under the last document, where the Telegram apps put a comment on files.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The messages as posted, in order.</returns>
    /// <exception cref="ArgumentException">
    /// Not 2 to 10 documents, an empty one or one with a blank name, or <paramref name="caption"/> longer than the
    /// 1024 characters of a caption.
    /// </exception>
    /// <remarks>When the bot fails on an item, the rest are not sent.</remarks>
    public Task<IReadOnlyList<TestMessage>> SendsDocumentAlbumAsync(
        IReadOnlyList<TestDocument> documents,
        string? caption = null,
        CancellationToken token = default
    )
    {
        ThrowIfNotAnAlbum(documents);
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document, nameof(documents));
            ThrowIfEmpty(document.Content, nameof(documents));
            ArgumentException.ThrowIfNullOrWhiteSpace(document.FileName, nameof(documents));
        }

        return SendsAlbumAsync(
            [.. documents.Select(document => DocumentOf(document.Content, document.FileName, document.MimeType))],
            CheckedCaption(caption),
            captionOn: documents.Count - 1,
            token
        );
    }

    /// <summary>
    /// Edits <paramref name="message"/>, the user's own text message as it now stands, to <paramref name="text"/>
    /// trimmed as Telegram does: the chat shows the edit, and the bot gets an <c>edited_message</c> update.
    /// </summary>
    /// <param name="message">The user's text message; captions cannot be edited this way.</param>
    /// <param name="text">The new text.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The edited message.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is blank, longer than the 4096 characters of a message, or unchanged.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not the user's, is no longer in the chat, or has no text.
    /// </exception>
    public async Task<TestMessage> EditsAsync(TestMessage message, string text, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        text = CheckedText(text);
        ThrowIfBlocked();

        var current = StillShown(message, Messages);
        if (current.Message.From?.Id != Id)
        {
            throw new InvalidOperationException($"{Quote(current)} is not {FirstName}'s: only its sender can edit it.");
        }

        if (current.Text is null)
        {
            throw new InvalidOperationException(
                $"{Quote(current)} has no text to edit; EditsAsync edits text, and captions cannot be edited this way."
            );
        }

        if (current.Text == text)
        {
            throw new ArgumentException("Telegram sends no edit for an unchanged message.", nameof(text));
        }

        // An edit reaches the bot only when the message did.
        if (!_host.Api.WasHeard(Chat.Id, current.Id))
        {
            _host.Api.ThrowIfNotAllowed("edited_message");
            return new TestMessage(_host.Api.EditByUser(Chat.Id, current.Id, text), _host.Api);
        }

        TestMessage? edited = null;
        await _host.DeliverAsync(
            "edited_message",
            () =>
            {
                var json = _host.Api.EditByUser(Chat.Id, current.Id, text);
                edited = new TestMessage(json.DeepClone().AsObject(), _host.Api);
                return new JsonObject { ["edited_message"] = json };
            },
            token
        );

        return edited!;
    }

    /// <summary>Taps the inline button <paramref name="button"/> on the newest message showing it.</summary>
    /// <param name="button">The button's exact text.</param>
    /// <param name="on">A specific message to tap it on, as that message now stands.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// No such button is shown, it is ambiguous, or it is not a callback button.
    /// </exception>
    public Task<TestCallbackAnswer> TapsAsync(string button, TestMessage? on = null, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(button);

        return TapAsync(x => x.Text == button, $"\"{button}\" button", on, asShown: false, token);
    }

    /// <summary>
    /// Taps <paramref name="button"/> on <paramref name="on"/> as the user's app showed it, or as it now stands.
    /// </summary>
    /// <param name="button">The button's exact text.</param>
    /// <param name="on">The message to tap it on.</param>
    /// <param name="asShown">Whether to find the button on the snapshot <paramref name="on"/>, as a stale view would.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// The message is from another chat, or the button isn't shown, is ambiguous, or is not a callback button.
    /// </exception>
    /// <remarks>
    /// With <paramref name="asShown"/>, the bot gets the message as it now stands, or only its chat and id once
    /// deleted.
    /// </remarks>
    public Task<TestCallbackAnswer> TapsAsync(
        string button,
        TestMessage on,
        bool asShown,
        CancellationToken token = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(button);
        ArgumentNullException.ThrowIfNull(on);

        return TapAsync(x => x.Text == button, $"\"{button}\" button", on, asShown, token);
    }

    /// <summary>
    /// Taps the one inline button <paramref name="button"/> picks, e.g. by its callback data where labels repeat, on
    /// the newest message showing a match.
    /// </summary>
    /// <param name="button">Picks the button, e.g. <c>b =&gt; b.CallbackData == "date:next"</c>.</param>
    /// <param name="on">A specific message to tap it on, as that message now stands.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// No button matches, more than one on the message does, or the match is not a callback button.
    /// </exception>
    public Task<TestCallbackAnswer> TapsAsync(
        Func<InlineKeyboardButton, bool> button,
        TestMessage? on = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(button);

        return TapAsync(button, "button matching the predicate", on, asShown: false, token);
    }

    /// <summary>
    /// Taps the one inline button <paramref name="button"/> picks on <paramref name="on"/>, as the user's app showed
    /// it or as it now stands.
    /// </summary>
    /// <param name="button">Picks the button, e.g. <c>b =&gt; b.CallbackData == "date:next"</c>.</param>
    /// <param name="on">The message to tap it on.</param>
    /// <param name="asShown">Whether to find the button on the snapshot <paramref name="on"/>, as a stale view would.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// The message is from another chat, or not exactly one callback button on it matches.
    /// </exception>
    /// <remarks>
    /// With <paramref name="asShown"/>, the bot gets the message as it now stands, or only its chat and id once
    /// deleted.
    /// </remarks>
    public Task<TestCallbackAnswer> TapsAsync(
        Func<InlineKeyboardButton, bool> button,
        TestMessage on,
        bool asShown,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(on);

        return TapAsync(button, "button matching the predicate", on, asShown, token);
    }

    /// <summary>
    /// Taps the button whose data decodes as <typeparamref name="TButton"/> and matches <paramref name="which"/>, on
    /// the newest message showing one.
    /// </summary>
    /// <typeparam name="TButton">A <c>[ButtonData]</c> struct.</typeparam>
    /// <param name="which">Picks the button by its data; any, when <c>null</c>.</param>
    /// <param name="on">A specific message to tap it on, as that message now stands.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TButton"/> can't be button data, or not exactly one button on the message matches.
    /// </exception>
    public Task<TestCallbackAnswer> TapsAsync<TButton>(
        Func<TButton, bool>? which = null,
        TestMessage? on = null,
        CancellationToken token = default
    )
        where TButton : struct => TapTypedAsync(which, on, asShown: false, token);

    /// <summary>
    /// Taps the button whose data decodes as <typeparamref name="TButton"/> and matches <paramref name="which"/>, on
    /// <paramref name="on"/> as the user's app showed it or as it now stands.
    /// </summary>
    /// <typeparam name="TButton">A <c>[ButtonData]</c> struct.</typeparam>
    /// <param name="which">Picks the button by its data; any, when <c>null</c>.</param>
    /// <param name="on">The message to tap it on.</param>
    /// <param name="asShown">Whether to find the button on the snapshot <paramref name="on"/>, as a stale view would.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>The bot's answer: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TButton"/> can't be button data, the message is from another chat, or not exactly one
    /// button on it matches.
    /// </exception>
    /// <remarks>
    /// With <paramref name="asShown"/>, the bot gets the message as it now stands, or only its chat and id once
    /// deleted.
    /// </remarks>
    public Task<TestCallbackAnswer> TapsAsync<TButton>(
        Func<TButton, bool>? which,
        TestMessage on,
        bool asShown,
        CancellationToken token = default
    )
        where TButton : struct
    {
        ArgumentNullException.ThrowIfNull(on);

        return TapTypedAsync(which, on, asShown, token);
    }

    private Task<TestCallbackAnswer> TapTypedAsync<TButton>(
        Func<TButton, bool>? which,
        TestMessage? on,
        bool asShown,
        CancellationToken token
    )
        where TButton : struct
    {
        // Fails at once for a type that can't be button data.
        ButtonData.TryDecode<TButton>(null, out _);

        var type = typeof(TButton).Name;
        var what = which is null ? $"{type} button" : $"{type} button matching the predicate";

        return TapAsync(
            button => Decode(button, out TButton data) && (which?.Invoke(data) ?? true),
            what,
            on,
            asShown,
            token,
            (message, matches) =>
                $"{message} shows more than one {what}, so which one {FirstName} taps is ambiguous: "
                + $"{string.Join(", ", matches.Select(ValueOf))}. Pick one with TapsAsync<{type}>(b => …)."
        );

        static bool Decode(InlineKeyboardButton button, out TButton data)
        {
            data = default;
            return button.CallbackData is { } callbackData && ButtonData.TryDecode(callbackData, out data);
        }

        static string? ValueOf(InlineKeyboardButton button) =>
            Decode(button, out TButton data) ? data.ToString() : null;
    }

    // Finds the button on `on` as shown, as it now stands, or (on null) on the newest message showing one; the bot
    // gets the message as it now stands. `ambiguous` words the error for a message and its matches.
    private Task<TestCallbackAnswer> TapAsync(
        Func<InlineKeyboardButton, bool> match,
        string what,
        TestMessage? on,
        bool asShown,
        CancellationToken token,
        Func<string, InlineKeyboardButton[], string>? ambiguous = null
    )
    {
        if (!asShown || on is null)
        {
            var (message, data) = Find(match, what, on, ambiguous);
            return TapAsync(message.ToJson(), data, token);
        }

        if (on.Message.Chat.Id != Chat.Id)
        {
            throw new InvalidOperationException($"{Quote(on)} is not in {Chat.Description}.");
        }

        var shown = $"as {FirstName}'s app showed it";
        var (_, shownData) = Pick(match, what, [on], $"on {Quote(on)} {shown}", $", {shown},", ambiguous);

        // Telegram sends a deleted message as an inaccessible one: its chat and id, dated 0.
        var current =
            _host.Api.MessageIn(Chat.Id, on.Id)
            ?? new JsonObject
            {
                ["chat"] = on.ToJson()["chat"]!.DeepClone(),
                ["message_id"] = on.Id,
                ["date"] = 0,
            };

        return TapAsync(current, shownData, token);
    }

    private async Task<TestCallbackAnswer> TapAsync(JsonObject message, string data, CancellationToken token)
    {
        ThrowIfBlocked();
        var queryId = _host.Api.NextCallbackQueryId();
        var query = new JsonObject
        {
            ["id"] = queryId,
            ["from"] = Person,
            ["message"] = message,
            ["chat_instance"] = Chat.Id.ToString(),
            ["data"] = data,
        };

        await _host.DeliverAsync("callback_query", () => new JsonObject { ["callback_query"] = query }, token);
        return new TestCallbackAnswer(_host.Api.CallbackAnswer(queryId));
    }

    /// <summary>The user's first name.</summary>
    public override string ToString() => FirstName;

    // Trimmed as Telegram does, and within a message's limit.
    private static string CheckedText(string text, [CallerArgumentExpression(nameof(text))] string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, name);

        text = text.Trim();
        if (text.Length > FakeBotApi.TextLimit)
        {
            throw new ArgumentException(
                $"Telegram takes at most {FakeBotApi.TextLimit} characters in a message, and this text has "
                    + $"{text.Length}: the Telegram app splits longer text into several messages; send them one "
                    + "by one.",
                name
            );
        }

        return text;
    }

    // Trimmed as Telegram does, null when empty, and within a caption's limit.
    private static string? CheckedCaption(string? caption)
    {
        caption = caption?.Trim();
        if (caption?.Length > FakeBotApi.CaptionLimit)
        {
            throw new ArgumentException(
                $"Telegram takes at most {FakeBotApi.CaptionLimit} characters in a caption, and this one has "
                    + $"{caption.Length}.",
                nameof(caption)
            );
        }

        return string.IsNullOrEmpty(caption) ? null : caption;
    }

    private Func<JsonObject> PhotoOf(byte[] photo) => () => _host.Api.UserPhoto(photo);

    private Func<JsonObject> DocumentOf(byte[] document, string fileName, string? mimeType = null) =>
        () => _host.Api.UserDocument(document, fileName, mimeType);

    // As Telegram: a caption, already checked, carries a leading /command marked.
    private Task<TestMessage> SendsFileAsync(Func<JsonObject> file, string? caption, CancellationToken token) =>
        DeliverAsync(
            new JsonObject { ["caption"] = caption },
            () =>
            {
                var content = file();
                if (caption is not null)
                {
                    content["caption"] = caption;

                    if (FakeBotApi.BotCommandEntities(caption) is { } entities)
                    {
                        content["caption_entities"] = entities;
                    }
                }

                return _host.Api.Receive(Chat.Id, Person, content);
            },
            token
        );

    // Each item is its own update, sent once the bot has handled the one before.
    private async Task<IReadOnlyList<TestMessage>> SendsAlbumAsync(
        Func<JsonObject>[] items,
        string? caption,
        int captionOn,
        CancellationToken token
    )
    {
        var mediaGroupId = _host.Api.NextMediaGroupId();
        var sent = new List<TestMessage>();

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            sent.Add(
                await SendsFileAsync(
                    () =>
                    {
                        var content = item();
                        content["media_group_id"] = mediaGroupId;
                        return content;
                    },
                    index == captionOn ? caption : null,
                    token
                )
            );
        }

        return sent;
    }

    private static void ThrowIfNotAnAlbum<T>(
        IReadOnlyList<T> items,
        [CallerArgumentExpression(nameof(items))] string? name = null
    )
    {
        ArgumentNullException.ThrowIfNull(items, name);

        if (items.Count is < 2 or > 10)
        {
            throw new ArgumentException($"An album holds 2 to 10 items, not {items.Count}.", name);
        }
    }

    // The message is posted only once the bot can take it; returns it as posted.
    // preview holds what decides whether Telegram sends the message to the bot: its text or caption, and what it
    // replies to.
    private async Task<TestMessage> DeliverAsync(JsonObject preview, Func<JsonObject> message, CancellationToken token)
    {
        ThrowIfBlocked();
        if (!_host.Api.WouldDeliver(Chat.Id, preview))
        {
            // Group privacy keeps it from the bot: it is only posted, and nothing waits for the bot.
            _host.Api.ThrowIfNotAllowed("message");
            var json = message();
            _host.Api.MarkUnheard(Chat.Id, json["message_id"]!.GetValue<int>());
            return new TestMessage(json.DeepClone().AsObject(), _host.Api);
        }

        TestMessage? posted = null;
        await _host.DeliverAsync(
            "message",
            () =>
            {
                var json = message();
                posted = new TestMessage(json.DeepClone().AsObject(), _host.Api);
                return new JsonObject { ["message"] = json };
            },
            token
        );

        return posted!;
    }

    private static void ThrowIfEmpty(byte[] content, [CallerArgumentExpression(nameof(content))] string? name = null) =>
        FakeBotApi.ThrowIfEmpty(content, name);

    private static string Quote(TestMessage message) => $"\"{message.Content}\"";

    // The one button `match` picks on `on` as it now stands, or on the newest message showing one.
    private (TestMessage Message, string Data) Find(
        Func<InlineKeyboardButton, bool> match,
        string what,
        TestMessage? on,
        Func<string, InlineKeyboardButton[], string>? ambiguous
    )
    {
        var messages = Messages;

        return on is null
            ? Pick(match, what, messages.Reverse(), $"in {Chat.Description}", "", ambiguous)
            : Pick(match, what, [StillShown(on, messages)], $"on {Quote(on)}", "", ambiguous);
    }

    // The one button `match` picks on the first of `candidates` showing one. `what`, `where` (for none) and `ambiguous`
    // word errors; `aside` follows a quoted message in them.
    private (TestMessage Message, string Data) Pick(
        Func<InlineKeyboardButton, bool> match,
        string what,
        IEnumerable<TestMessage> candidates,
        string where,
        string aside,
        Func<string, InlineKeyboardButton[], string>? ambiguous
    )
    {
        foreach (var message in candidates)
        {
            var matches = message.Keyboard.Where(match).ToArray();
            switch (matches)
            {
                case []:
                    continue;
                case [{ CallbackData: { } data }]:
                    return (message, data);
                case [var button]:
                    throw new InvalidOperationException(
                        $"The \"{button.Text}\" button is not a callback button: the Telegram app handles it, and the "
                            + "bot never hears of the tap."
                    );
                case [_, _, ..] when ambiguous is not null:
                    throw new InvalidOperationException(ambiguous(Quote(message) + aside, matches));
                case [var first, ..]:
                    throw new InvalidOperationException(
                        $"{Quote(message)}{aside} shows more than one {what}, so which one {FirstName} taps is "
                            + "ambiguous. "
                            + $"Pick one with TapsAsync(b => b.CallbackData == \"{first.CallbackData ?? "…"}\")."
                    );
            }
        }

        var shown = candidates.SelectMany(message => message.Buttons).Distinct().ToArray();
        throw new InvalidOperationException(
            $"{FirstName} sees no {what} {where}. "
                + (shown.Length == 0 ? "There are no buttons." : $"The buttons are \"{string.Join("\", \"", shown)}\".")
        );
    }

    // `on` as it now stands.
    private TestMessage StillShown(TestMessage on, IReadOnlyList<TestMessage> messages)
    {
        if (on.Message.Chat.Id != Chat.Id)
        {
            throw new InvalidOperationException($"{Quote(on)} is not in {Chat.Description}.");
        }

        return messages.FirstOrDefault(message => message.Id == on.Id)
            ?? throw new InvalidOperationException($"{Quote(on)} is no longer in {Chat.Description}.");
    }
}
