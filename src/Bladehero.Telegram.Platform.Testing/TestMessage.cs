using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A message in a <see cref="TestChat"/> as it stood when read — with the bot's edits applied. It does not change
/// afterwards: read the chat again to see what the bot did next.
/// </summary>
public sealed class TestMessage
{
    private readonly JsonObject _json;

    internal TestMessage(JsonObject json)
    {
        _json = json;
        Message = json.Deserialize<Message>(JsonBotAPI.Options)!;
    }

    public int Id => Message.Id;

    public string? Text => Message.Text;

    /// <summary>The text under a photo or file.</summary>
    public string? Caption => Message.Caption;

    public bool IsFromBot => Message.From?.IsBot is true;

    public bool IsEdited => Message.EditDate is not null;

    /// <summary>The texts of the inline keyboard's buttons, row by row.</summary>
    public IReadOnlyList<string> Buttons => [.. Keyboard.Select(button => button.Text)];

    /// <summary>The message as Telegram.Bot reads it, for anything the properties above do not cover.</summary>
    public Message Message { get; }

    internal IEnumerable<InlineKeyboardButton> Keyboard =>
        Message.ReplyMarkup?.InlineKeyboard.SelectMany(row => row) ?? [];

    internal JsonObject ToJson() => _json.DeepClone().AsObject();

    // What the message shows, without its sender or buttons: "(photo) Lunch", "What size?".
    internal string Content => string.Join(" ", new[] { Attachment, Text ?? Caption }.OfType<string>());

    // Reads like the chat: "Nick: (photo) Lunch", "Bot: What size? [Small] [Large]".
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
