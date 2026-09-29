using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
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
public sealed class TestUser
{
    // The small photo size, so a bot reading Photo[0] instead of the largest gets the wrong bytes, as with Telegram.
    private static readonly byte[] Thumbnail = "thumbnail"u8.ToArray();

    private readonly TelegramTestHost _host;
    private readonly JsonObject _person;

    internal TestUser(TelegramTestHost host, JsonObject person, TestChat chat)
    {
        _host = host;
        _person = person;
        Chat = chat;
    }

    /// <summary>The Telegram user id, the same in every chat.</summary>
    public long Id => _person["id"]!.GetValue<long>();

    public string FirstName => _person["first_name"]!.GetValue<string>();

    public TestChat Chat { get; }

    /// <inheritdoc cref="TestChat.Messages"/>
    public IReadOnlyList<TestMessage> Messages => Chat.Messages;

    /// <inheritdoc cref="TestChat.LastMessage"/>
    public TestMessage LastMessage => Chat.LastMessage;

    /// <summary>Sends <paramref name="text"/>, trimmed as Telegram does.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is blank, or longer than the 4096 characters of a Telegram message.
    /// </exception>
    public Task SendsAsync(string text, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        text = text.Trim();
        if (text.Length > FakeBotApi.TextLimit)
        {
            throw new ArgumentException(
                $"Telegram takes at most {FakeBotApi.TextLimit} characters in a message, and this text has "
                    + $"{text.Length}: the Telegram app splits longer text into several messages; send them one by one.",
                nameof(text)
            );
        }

        return DeliverAsync(() => _host.Api.Receive(Chat.Id, _person, text), token);
    }

    /// <summary>
    /// Sends a photo in two sizes, smallest first, as Telegram does; the largest downloads as <paramref name="photo"/>.
    /// Any bytes will do; the reported dimensions are nominal.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="photo"/> is empty, or <paramref name="caption"/> longer than the 1024 characters of a caption.
    /// </exception>
    public Task SendsPhotoAsync(byte[] photo, string? caption = null, CancellationToken token = default)
    {
        ThrowIfEmpty(photo);

        return SendsFileAsync(
            () =>
            {
                var thumbnail = _host.Api.StoreFile(
                    FileKind.Photo,
                    Thumbnail,
                    new JsonObject { ["width"] = 90, ["height"] = 68 }
                )["photo"]![0]!;

                var content = _host.Api.StoreFile(
                    FileKind.Photo,
                    photo,
                    new JsonObject { ["width"] = 1280, ["height"] = 960 }
                );
                content["photo"]!.AsArray().Insert(0, thumbnail.DeepClone());
                return content;
            },
            caption,
            token
        );
    }

    /// <summary>Sends a voice message of <paramref name="duration"/> (one second by default).</summary>
    /// <exception cref="ArgumentException"><paramref name="voice"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public Task SendsVoiceAsync(byte[] voice, TimeSpan? duration = null, CancellationToken token = default)
    {
        ThrowIfEmpty(voice);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration ?? TimeSpan.Zero, TimeSpan.Zero, nameof(duration));

        return SendsFileAsync(
            () =>
                _host.Api.StoreFile(
                    FileKind.Voice,
                    voice,
                    new JsonObject
                    {
                        ["duration"] = (int)Math.Ceiling((duration ?? TimeSpan.FromSeconds(1)).TotalSeconds),
                        ["mime_type"] = "audio/ogg",
                    }
                ),
            caption: null,
            token
        );
    }

    /// <summary>
    /// Sends <paramref name="document"/> named <paramref name="fileName"/>. The MIME type comes from the extension
    /// (pdf, txt, csv, json, xml, zip, jpg/jpeg, png, webp, gif, heic, ogg/oga, mp3, mp4, docx, xlsx; otherwise
    /// octet-stream) unless <paramref name="mimeType"/> is given.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/> is empty, <paramref name="fileName"/> blank, or <paramref name="caption"/> longer
    /// than the 1024 characters of a caption.
    /// </exception>
    public Task SendsDocumentAsync(
        byte[] document,
        string fileName,
        string? caption = null,
        string? mimeType = null,
        CancellationToken token = default
    )
    {
        ThrowIfEmpty(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return SendsFileAsync(
            () =>
                _host.Api.StoreFile(
                    FileKind.Document,
                    document,
                    new JsonObject
                    {
                        ["file_name"] = fileName,
                        ["mime_type"] = string.IsNullOrWhiteSpace(mimeType) ? MimeTypes.Of(fileName) : mimeType,
                    }
                ),
            caption,
            token
        );
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

        return TapAsync(Find(x => x.Text == button, $"\"{button}\" button", on), token);
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

        return TapAsync(Find(button, "button matching the predicate", on), token);
    }

    private async Task<TestCallbackAnswer> TapAsync((TestMessage Message, string Data) tap, CancellationToken token)
    {
        var (message, data) = tap;
        var queryId = _host.Api.NextCallbackQueryId();
        var query = new JsonObject
        {
            ["id"] = queryId,
            ["from"] = _person.DeepClone(),
            ["message"] = message.ToJson(),
            ["chat_instance"] = Chat.Id.ToString(),
            ["data"] = data,
        };

        await _host.DeliverAsync("callback_query", () => new JsonObject { ["callback_query"] = query }, token);
        return new TestCallbackAnswer(_host.Api.CallbackAnswer(queryId));
    }

    public override string ToString() => FirstName;

    // As Telegram: captions are trimmed, empty ones dropped, and a leading /command is marked.
    private Task SendsFileAsync(Func<JsonObject> file, string? caption, CancellationToken token)
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

        return DeliverAsync(
            () =>
            {
                var content = file();
                if (!string.IsNullOrEmpty(caption))
                {
                    content["caption"] = caption;

                    if (FakeBotApi.BotCommandEntities(caption) is { } entities)
                    {
                        content["caption_entities"] = entities;
                    }
                }

                return _host.Api.Receive(Chat.Id, _person, content);
            },
            token
        );
    }

    // The message is posted only once the bot can take it.
    private Task DeliverAsync(Func<JsonObject> message, CancellationToken token) =>
        _host.DeliverAsync("message", () => new JsonObject { ["message"] = message() }, token);

    private static void ThrowIfEmpty(byte[] content, [CallerArgumentExpression(nameof(content))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(content, name);

        if (content.Length == 0)
        {
            throw new ArgumentException("The Telegram app never sends an empty file.", name);
        }
    }

    private static string Quote(TestMessage message) => $"\"{message.Content}\"";

    // The one button `match` picks on `on`, or on the newest message showing a match; `what` names it in errors.
    private (TestMessage Message, string Data) Find(
        Func<InlineKeyboardButton, bool> match,
        string what,
        TestMessage? on
    )
    {
        var messages = Messages;
        IEnumerable<TestMessage> candidates = on is null ? messages.Reverse() : [Current(on, messages)];

        foreach (var message in candidates)
        {
            switch (message.Keyboard.Where(match).ToArray())
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
                case [var first, ..]:
                    throw new InvalidOperationException(
                        $"{Quote(message)} shows more than one {what}, so which one {FirstName} taps is ambiguous. "
                            + $"Pick one with TapsAsync(b => b.CallbackData == \"{first.CallbackData ?? "…"}\")."
                    );
            }
        }

        var shown = candidates.SelectMany(message => message.Buttons).Distinct().ToArray();
        var where = on is null ? $"in {Chat.Description}" : $"on {Quote(on)}";
        throw new InvalidOperationException(
            $"{FirstName} sees no {what} {where}. "
                + (shown.Length == 0 ? "There are no buttons." : $"The buttons are \"{string.Join("\", \"", shown)}\".")
        );
    }

    private TestMessage Current(TestMessage on, IReadOnlyList<TestMessage> messages)
    {
        if (on.Message.Chat.Id != Chat.Id)
        {
            throw new InvalidOperationException($"{Quote(on)} is not in {Chat.Description}.");
        }

        return messages.FirstOrDefault(message => message.Id == on.Id)
            ?? throw new InvalidOperationException($"{Quote(on)} is no longer in {Chat.Description}.");
    }
}
