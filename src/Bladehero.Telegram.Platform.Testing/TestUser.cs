using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A person in a <see cref="TestChat"/>, doing what a user does in the Telegram app: typing messages, sending files and
/// tapping the bot's buttons. Each action returns once the bot has finished handling it, and rethrows whatever a
/// command threw.
/// </summary>
public sealed class TestUser
{
    // What the smaller size of a photo downloads as: not the photo, so a bot that takes the first size instead of the
    // last gets the wrong bytes, as it would get a thumbnail from Telegram.
    private static readonly byte[] Thumbnail = "thumbnail"u8.ToArray();

    private readonly TelegramTestHost _host;
    private readonly JsonObject _person;

    internal TestUser(TelegramTestHost host, JsonObject person, TestChat chat)
    {
        _host = host;
        _person = person;
        Chat = chat;
    }

    /// <summary>The person's Telegram user id — the same in every chat they are in.</summary>
    public long Id => _person["id"]!.GetValue<long>();

    public string FirstName => _person["first_name"]!.GetValue<string>();

    public TestChat Chat { get; }

    /// <inheritdoc cref="TestChat.Messages"/>
    public IReadOnlyList<TestMessage> Messages => Chat.Messages;

    /// <inheritdoc cref="TestChat.LastMessage"/>
    public TestMessage LastMessage => Chat.LastMessage;

    /// <summary>Sends <paramref name="text"/> to the chat, trimmed as Telegram trims it.</summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank, which the app never sends.</exception>
    public Task SendsAsync(string text, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return DeliverAsync(_host.Api.Receive(Chat.Id, _person, text.Trim()), token);
    }

    /// <summary>
    /// Sends a photo. Like Telegram, the message carries it in two sizes, smallest first: a thumbnail, then
    /// <paramref name="photo"/> itself, which the bot can download back byte for byte from the last, largest size.
    /// The fake does not look inside the bytes, so any will do and the dimensions it reports are nominal.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="photo"/> is empty, which the app never sends.</exception>
    public Task SendsPhotoAsync(byte[] photo, string? caption = null, CancellationToken token = default)
    {
        ThrowIfEmpty(photo);

        var thumbnail = _host.Api.StoreFile(
            FileKind.Photo,
            Thumbnail,
            new JsonObject { ["width"] = 90, ["height"] = 68 }
        )["photo"]![0]!;

        var content = _host.Api.StoreFile(FileKind.Photo, photo, new JsonObject { ["width"] = 1280, ["height"] = 960 });
        content["photo"]!.AsArray().Insert(0, thumbnail.DeepClone());

        return SendsFileAsync(content, caption, token);
    }

    /// <summary>Sends a voice message, recorded for <paramref name="duration"/> — a second when not given.</summary>
    /// <exception cref="ArgumentException"><paramref name="voice"/> is empty, which the app never sends.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public Task SendsVoiceAsync(byte[] voice, TimeSpan? duration = null, CancellationToken token = default)
    {
        ThrowIfEmpty(voice);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration ?? TimeSpan.Zero, TimeSpan.Zero, nameof(duration));

        var content = _host.Api.StoreFile(
            FileKind.Voice,
            voice,
            new JsonObject
            {
                ["duration"] = (int)Math.Ceiling((duration ?? TimeSpan.FromSeconds(1)).TotalSeconds),
                ["mime_type"] = "audio/ogg",
            }
        );

        return SendsFileAsync(content, caption: null, token);
    }

    /// <summary>
    /// Sends <paramref name="document"/> as a file named <paramref name="fileName"/>. Its MIME type is worked out from
    /// the name's extension, as the app does, unless <paramref name="mimeType"/> is given. Only common extensions are
    /// known — PDF, text, CSV, JSON, XML, ZIP, JPEG, PNG, DOCX and XLSX — and any other is sent as
    /// <c>application/octet-stream</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/> is empty, which the app never sends, or <paramref name="fileName"/> is blank.
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

        var content = _host.Api.StoreFile(
            FileKind.Document,
            document,
            new JsonObject
            {
                ["file_name"] = fileName,
                ["mime_type"] = string.IsNullOrWhiteSpace(mimeType) ? MimeTypes.Of(fileName) : mimeType,
            }
        );

        return SendsFileAsync(content, caption, token);
    }

    /// <summary>
    /// Taps the inline button labelled <paramref name="button"/> on the newest message showing it, and the bot
    /// receives the button's callback query.
    /// </summary>
    /// <param name="button">The button's text, exactly as the bot sent it.</param>
    /// <param name="on">The message to tap the button on, when an older message shows the same button.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>How the bot answered the tap: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// No message shows the button, the message shows it twice, or the button sends the bot nothing — a link, say.
    /// </exception>
    public async Task<TestCallbackAnswer> TapsAsync(
        string button,
        TestMessage? on = null,
        CancellationToken token = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(button);

        var (message, data) = Find(button, on);
        var queryId = _host.Api.NextCallbackQueryId();
        var query = new JsonObject
        {
            ["id"] = queryId,
            ["from"] = _person.DeepClone(),
            ["message"] = message.ToJson(),
            ["chat_instance"] = Chat.Id.ToString(),
            ["data"] = data,
        };

        await _host.DeliverAsync(new JsonObject { ["callback_query"] = query }, token);
        return new TestCallbackAnswer(_host.Api.CallbackAnswer(queryId));
    }

    public override string ToString() => FirstName;

    // Telegram trims a caption and drops an empty one, and marks a bot command at its start as it does in a text.
    private Task SendsFileAsync(JsonObject content, string? caption, CancellationToken token)
    {
        caption = caption?.Trim();
        if (!string.IsNullOrEmpty(caption))
        {
            content["caption"] = caption;

            if (FakeBotApi.BotCommandEntities(caption) is { } entities)
            {
                content["caption_entities"] = entities;
            }
        }

        return DeliverAsync(_host.Api.Receive(Chat.Id, _person, content), token);
    }

    private Task DeliverAsync(JsonObject message, CancellationToken token) =>
        _host.DeliverAsync(new JsonObject { ["message"] = message }, token);

    private static void ThrowIfEmpty(byte[] content, [CallerArgumentExpression(nameof(content))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(content, name);

        if (content.Length == 0)
        {
            throw new ArgumentException("The Telegram app never sends an empty file.", name);
        }
    }

    private static string Quote(TestMessage message) => $"\"{message.Content}\"";

    private (TestMessage Message, string Data) Find(string button, TestMessage? on)
    {
        var messages = Messages;
        IEnumerable<TestMessage> candidates = on is null ? messages.Reverse() : [Current(on, messages)];

        foreach (var message in candidates)
        {
            switch (message.Keyboard.Where(x => x.Text == button).ToArray())
            {
                case []:
                    continue;
                case [{ CallbackData: { } data }]:
                    return (message, data);
                case [_]:
                    throw new InvalidOperationException(
                        $"The \"{button}\" button is not a callback button: the Telegram app handles it, and the bot "
                            + "never hears of the tap."
                    );
                default:
                    throw new InvalidOperationException(
                        $"{Quote(message)} shows more than one \"{button}\" button, so which one {FirstName} taps "
                            + "is ambiguous."
                    );
            }
        }

        var shown = candidates.SelectMany(message => message.Buttons).Distinct().ToArray();
        var where = on is null ? $"in {Chat.Description}" : $"on {Quote(on)}";
        throw new InvalidOperationException(
            $"{FirstName} sees no \"{button}\" button {where}. "
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
