using System.Text;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Testing.Formatting;

namespace Bladehero.Telegram.Platform.Testing;

// Texts and captions as Telegram keeps them: parse_mode rendered, cleaned and trimmed, and measured in characters.
// Cut: URL checks and normalisation, fixing nested entities, auto-detected url, mention and hashtag entities, custom
// emoji and premium rules, tg-time, text_mention, and checks of explicit entities.
public sealed partial class FakeBotApi
{
    private const int RawTextLimit = 32768;

    internal enum TextKind
    {
        Text,
        Caption,
    }

    internal enum Operation
    {
        Send,
        Edit,
    }

    // The stored text and entities, or nulls for no caption; refused as Telegram refuses it.
    private static (string? Value, JsonArray? Entities) Formatted(
        JsonNode? value,
        JsonNode? parseMode,
        JsonNode? entities,
        TextKind kind,
        Operation operation
    )
    {
        Func<string, (string, List<JsonObject>)>? parse = parseMode?.GetValue<string>().ToLowerInvariant() switch
        {
            null or "" or "none" => null,
            "html" => HtmlParser.Parse,
            "markdownv2" => MarkdownV2Parser.Parse,
            "markdown" => MarkdownParser.Parse,
            _ => throw Refuse(400, "Bad Request: unsupported parse_mode"),
        };

        var raw = value?.GetValue<string>();
        if (raw is not null && Encoding.UTF8.GetByteCount(raw) > RawTextLimit)
        {
            throw Refuse(400, "Bad Request: text is too long");
        }

        if (string.IsNullOrEmpty(raw))
        {
            return kind == TextKind.Text ? throw Refuse(400, "Bad Request: message text is empty") : (null, null);
        }

        string text;
        List<JsonObject> found;
        try
        {
            (text, found) = parse is null ? (raw, [.. (entities as JsonArray ?? []).OfType<JsonObject>()]) : parse(raw);
        }
        catch (FormattingException refused)
        {
            throw Refuse(400, $"Bad Request: can't parse entities: {refused.Message}");
        }

        (text, found) = TextCleaner.Clean(text, found);
        (text, found) = TextCleaner.Trim(text, found);

        if (text.Length == 0)
        {
            return kind == TextKind.Text ? throw Refuse(400, "Bad Request: text must be non-empty") : (null, null);
        }

        var (limit, tooLong) = (kind, operation) switch
        {
            (TextKind.Text, Operation.Send) => (TextLimit, "Bad Request: message is too long"),
            (TextKind.Text, _) => (TextLimit, "Bad Request: MESSAGE_TOO_LONG"),
            (_, Operation.Send) => (CaptionLimit, "Bad Request: message caption is too long"),
            _ => (CaptionLimit, "Bad Request: MEDIA_CAPTION_TOO_LONG"),
        };

        if (text.EnumerateRunes().Count() > limit)
        {
            throw Refuse(400, tooLong);
        }

        return (text, found.Count == 0 ? null : new JsonArray([.. found]));
    }
}
