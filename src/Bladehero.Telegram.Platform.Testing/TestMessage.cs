using System.Text.Json;
using System.Text.Json.Nodes;
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

    public int Id => Message.Id;

    public string? Text => Message.Text;

    /// <summary>The text under a photo or file.</summary>
    public string? Caption => Message.Caption;

    /// <summary>The photo (largest size), or <c>null</c>.</summary>
    public TestFile? Photo => Message.Photo is [.., var largest] ? _api.TestFileOf(largest.FileId) : null;

    /// <summary>The document, or <c>null</c>.</summary>
    public TestFile? Document => Message.Document is { } document ? _api.TestFileOf(document.FileId) : null;

    /// <summary>The voice message, or <c>null</c>.</summary>
    public TestFile? Voice => Message.Voice is { } voice ? _api.TestFileOf(voice.FileId) : null;

    public bool IsFromBot => Message.From?.IsBot is true;

    public bool IsEdited => Message.EditDate is not null;

    /// <summary>The inline keyboard's button texts, row by row.</summary>
    public IReadOnlyList<string> Buttons => [.. Keyboard.Select(button => button.Text)];

    /// <summary>The raw Telegram.Bot message.</summary>
    public Message Message { get; }

    internal IEnumerable<InlineKeyboardButton> Keyboard =>
        Message.ReplyMarkup?.InlineKeyboard.SelectMany(row => row) ?? [];

    internal JsonObject ToJson() => _json.DeepClone().AsObject();

    // "(photo) Lunch", "What size?"
    internal string Content => string.Join(" ", new[] { Attachment, Text ?? Caption }.OfType<string>());

    // "Nick: (photo) Lunch", "Bot: What size? [Small] [Large]"
    public override string ToString() =>
        $"{(IsFromBot ? "Bot" : Message.From?.FirstName)}:"
        + (Content.Length == 0 ? "" : $" {Content}")
        + string.Concat(Buttons.Select(button => $" [{button}]"));

    private string? Attachment =>
        Message switch
        {
            { Photo: [_, ..] } => "(photo)",
            { Voice: { } voice } => $"(voice {voice.Duration}s)",
            { Document: { } document } => $"(document {document.FileName})",
            _ => null,
        };
}
