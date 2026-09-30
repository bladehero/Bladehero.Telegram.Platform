using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing.Formatting;

// Markup Telegram can't parse; the message is TDLib's, after "can't parse entities: ".
internal sealed class FormattingException(string message) : Exception(message);

// What the parsers share: TDLib's byte-level character classes, UTF-16 counting and its entity order.
internal static class Markup
{
    public static bool IsSpace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0 or (byte)'\v';

    public static bool IsAlpha(byte c) => (c | 0x20) is >= 'a' and <= 'z';

    public static bool IsDigit(byte c) => c is >= (byte)'0' and <= (byte)'9';

    public static bool IsAlnum(byte c) => IsAlpha(c) || IsDigit(c);

    public static bool IsHexDigit(byte c) => IsDigit(c) || (c | 0x20) is >= 'a' and <= 'f';

    public static int HexValue(byte c) => IsDigit(c) ? c - '0' : (c | 0x20) - 'a' + 10;

    // The UTF-16 units a UTF-8 byte starts: none for a continuation byte, two for a four-byte character.
    public static int Utf16Units(byte c) =>
        (c & 0xC0) == 0x80 ? 0
        : c >= 0xF0 ? 2
        : 1;

    public static void AppendUtf8(List<byte> bytes, uint code)
    {
        Span<byte> buffer = stackalloc byte[4];
        var length = code switch
        {
            < 0x80 => Encode1(buffer, code),
            < 0x800 => Encode2(buffer, code),
            < 0x10000 => Encode3(buffer, code),
            _ => Encode4(buffer, code),
        };

        foreach (var b in buffer[..length])
        {
            bytes.Add(b);
        }
    }

    // ASCII only, as TDLib's to_lower.
    public static string Lower(byte[] text, int start, int length)
    {
        var bytes = text.AsSpan(start, length).ToArray();
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is >= (byte)'A' and <= (byte)'Z')
            {
                bytes[i] |= 0x20;
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static JsonObject Entity(string type, int offset, int length) =>
        new()
        {
            ["type"] = type,
            ["offset"] = offset,
            ["length"] = length,
        };

    // TDLib's order: by offset, longer first, then by type.
    public static List<JsonObject> Sorted(IEnumerable<JsonObject> entities) =>
        [
            .. entities
                .OrderBy(entity => entity["offset"]!.GetValue<int>())
                .ThenByDescending(entity => entity["length"]!.GetValue<int>())
                .ThenBy(Priority),
        ];

    private static int Priority(JsonObject entity) =>
        entity["type"]!.GetValue<string>() switch
        {
            "blockquote" or "expandable_blockquote" => 0,
            "pre" => entity.ContainsKey("language") ? 10 : 11,
            "code" => 20,
            "text_link" or "text_mention" => 49,
            "bold" => 90,
            "italic" => 91,
            "underline" => 92,
            "strikethrough" => 93,
            "spoiler" => 94,
            "custom_emoji" => 99,
            _ => 50,
        };

    private static int Encode1(Span<byte> buffer, uint code)
    {
        buffer[0] = (byte)code;
        return 1;
    }

    private static int Encode2(Span<byte> buffer, uint code)
    {
        buffer[0] = (byte)(0xC0 | (code >> 6));
        buffer[1] = (byte)(0x80 | (code & 0x3F));
        return 2;
    }

    private static int Encode3(Span<byte> buffer, uint code)
    {
        buffer[0] = (byte)(0xE0 | (code >> 12));
        buffer[1] = (byte)(0x80 | ((code >> 6) & 0x3F));
        buffer[2] = (byte)(0x80 | (code & 0x3F));
        return 3;
    }

    private static int Encode4(Span<byte> buffer, uint code)
    {
        buffer[0] = (byte)(0xF0 | (code >> 18));
        buffer[1] = (byte)(0x80 | ((code >> 12) & 0x3F));
        buffer[2] = (byte)(0x80 | ((code >> 6) & 0x3F));
        buffer[3] = (byte)(0x80 | (code & 0x3F));
        return 4;
    }
}
