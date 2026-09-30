using System.Text.Json;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A snapshot of a <see cref="TestChat"/> message, edits applied; read the chat again for later changes.
/// </summary>
public sealed class TestMessage
{
    private readonly JsonObject _json;
    private readonly FakeBotApi _api;

    internal TestMessage(JsonObject json, FakeBotApi api)
    {
        _json = json;
        _api = api;
        Message = json.Deserialize<Message>(JsonBotAPI.Options)!;
    }

    /// <summary>The message id, unique within its chat.</summary>
    public int Id => Message.Id;

    /// <summary>The text of a text message; <c>null</c> for a photo or file, whose text is its caption.</summary>
    public string? Text => Message.Text;

    /// <summary>The text under a photo or file.</summary>
    public string? Caption => Message.Caption;

    /// <summary>The photo (largest size), or <c>null</c>.</summary>
    public TestFile? Photo => Message.Photo is [.., var largest] ? _api.TestFileOf(largest.FileId) : null;

    /// <summary>The document, or <c>null</c>.</summary>
    public TestFile? Document => Message.Document is { } document ? _api.TestFileOf(document.FileId) : null;

    /// <summary>The voice message, or <c>null</c>.</summary>
    public TestFile? Voice => Message.Voice is { } voice ? _api.TestFileOf(voice.FileId) : null;

    /// <summary>Whether the bot sent it, rather than a test user.</summary>
    public bool IsFromBot => Message.From?.IsBot is true;

    /// <summary>Whether it was edited since it was sent, its text, caption or buttons.</summary>
    public bool IsEdited => Message.EditDate is not null;

    /// <summary>The inline keyboard's button texts, row by row.</summary>
    public IReadOnlyList<string> Buttons => [.. Keyboard.Select(button => button.Text)];

    /// <summary>The buttons whose data decodes as <typeparamref name="TButton"/>, row by row.</summary>
    /// <typeparam name="TButton">A <c>[ButtonData]</c> struct.</typeparam>
    /// <exception cref="InvalidOperationException"><typeparamref name="TButton"/> can't be button data.</exception>
    public IReadOnlyList<TButton> ButtonsOf<TButton>()
        where TButton : struct
    {
        // Fails at once for a type that can't be button data.
        ButtonData.TryDecode<TButton>(null, out _);

        var buttons = new List<TButton>();
        foreach (var button in Keyboard)
        {
            if (button.CallbackData is { } data && ButtonData.TryDecode(data, out TButton decoded))
            {
                buttons.Add(decoded);
            }
        }

        return buttons;
    }

    /// <summary>The raw Telegram.Bot message.</summary>
    public Message Message { get; }

    internal IEnumerable<InlineKeyboardButton> Keyboard =>
        Message.ReplyMarkup?.InlineKeyboard.SelectMany(row => row) ?? [];

    internal JsonObject ToJson() => _json.DeepClone().AsObject();

    // "(photo) Lunch", "What size?"
    internal string Content => string.Join(" ", new[] { Attachment, Text ?? Caption }.OfType<string>());

    /// <summary>
    /// The sender, the content and the buttons, e.g. <c>Nick: (photo) Lunch</c> or
    /// <c>Bot: What size? [Small] [Large]</c>; a reply adds what it replies to, as in
    /// <c>Nick (↩ Bot: What size?): Large</c>.
    /// </summary>
    public override string ToString() =>
        $"{Sender}{(ReplyTo is { } target ? $" (↩ {target.Sender}:{target.Spoken})" : "")}:"
        + Spoken
        + string.Concat(Buttons.Select(button => $" [{button}]"));

    /// <summary>The message this one replies to, or <c>null</c>.</summary>
    public TestMessage? ReplyTo =>
        _json["reply_to_message"] is JsonObject target ? new TestMessage(target.DeepClone().AsObject(), _api) : null;

    private string? Sender => IsFromBot ? "Bot" : Message.From?.FirstName;

    // The content after the sender, with its leading space.
    private string Spoken => Content.Length == 0 ? "" : $" {Content}";

    private string? Attachment =>
        Message switch
        {
            { Photo: [_, ..] } => "(photo)",
            { Voice: { } voice } => $"(voice {voice.Duration}s)",
            { Document: { } document } => $"(document {document.FileName})",
            _ => null,
        };
}
