using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

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
    /// <exception cref="ArgumentException"><paramref name="text"/> is blank.</exception>
    public Task SendsAsync(string text, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return DeliverAsync(() => _host.Api.Receive(Chat.Id, _person, text.Trim()), token);
    }

    /// <summary>
    /// Sends a photo in two sizes, smallest first, as Telegram does; the largest downloads as <paramref name="photo"/>.
    /// Any bytes will do; the reported dimensions are nominal.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="photo"/> is empty.</exception>
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
    /// (pdf, txt, csv, json, xml, zip, jpg/jpeg, png, docx, xlsx; otherwise octet-stream) unless
    /// <paramref name="mimeType"/> is given.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/> is empty or <paramref name="fileName"/> blank.
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

        await _host.DeliverAsync("callback_query", () => new JsonObject { ["callback_query"] = query }, token);
        return new TestCallbackAnswer(_host.Api.CallbackAnswer(queryId));
    }

    public override string ToString() => FirstName;

    // As Telegram: captions are trimmed, empty ones dropped, and a leading /command is marked.
    private Task SendsFileAsync(Func<JsonObject> file, string? caption, CancellationToken token)
    {
        caption = caption?.Trim();

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
