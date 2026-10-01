using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing.Formatting;

// TDLib's parse_html: plain text and entities from Telegram's HTML, with its error messages and byte offsets.
internal static class HtmlParser
{
    private static readonly HashSet<string> SupportedTags =
    [
        "a",
        "b",
        "strong",
        "i",
        "em",
        "s",
        "strike",
        "del",
        "u",
        "ins",
        "tg-spoiler",
        "tg-emoji",
        "tg-time",
        "span",
        "pre",
        "code",
        "blockquote",
    ];

    public static (string Text, List<JsonObject> Entities) Parse(string input)
    {
        var text = Encoding.UTF8.GetBytes(input);
        var result = new List<byte>(text.Length);
        var entities = new List<JsonObject>();
        var nested = new List<Open>();
        var utf16Offset = 0;
        var decodedSurrogate = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '&')
            {
                var position = i;
                var code = DecodeEntity(text, ref position);
                if (code != 0)
                {
                    i = position - 1;
                    utf16Offset += code > 0xFFFF ? 2 : 1;
                    decodedSurrogate |= code is >= 0xD800 and <= 0xDFFF;
                    Markup.AppendUtf8(result, code);
                    continue;
                }
            }

            if (c != '<')
            {
                utf16Offset += Markup.Utf16Units(c);
                result.Add(c);
                continue;
            }

            var begin = i++;
            if (At(text, i) != '/')
            {
                nested.Add(StartTag(text, begin, ref i, utf16Offset, result.Count));
                continue;
            }

            // An end tag.
            if (nested.Count == 0)
            {
                throw new FormattingException($"Unexpected end tag at byte offset {begin}");
            }

            while (!Markup.IsSpace(At(text, i)) && At(text, i) != '>')
            {
                i++;
            }

            var endTag = Markup.Lower(text, begin + 2, i - begin - 2);
            while (Markup.IsSpace(At(text, i)) && At(text, i) != 0)
            {
                i++;
            }

            if (At(text, i) != '>')
            {
                throw new FormattingException($"Unclosed end tag at byte offset {begin}");
            }

            var open = nested[^1];
            if (endTag.Length != 0 && endTag != open.Tag)
            {
                throw new FormattingException(
                    $"Unmatched end tag at byte offset {begin}, expected \"</{open.Tag}>\", found \"</{endTag}>\""
                );
            }

            if (utf16Offset > open.Offset)
            {
                AddEntity(entities, open, utf16Offset - open.Offset, result);
            }

            nested.RemoveAt(nested.Count - 1);
        }

        if (nested.Count != 0)
        {
            throw new FormattingException($"Can't find end tag corresponding to start tag \"{nested[^1].Tag}\"");
        }

        // A language counts only for code inside pre.
        foreach (var entity in entities.Where(entity => entity["type"]!.GetValue<string>() == "code"))
        {
            entity.Remove("language");
        }

        if (decodedSurrogate)
        {
            throw new FormattingException(
                "Text contains invalid Unicode characters after decoding HTML entities, check for unmatched "
                    + "surrogate code units"
            );
        }

        return (Encoding.UTF8.GetString([.. result]), Markup.Sorted(entities));
    }

    // Reads a start tag from its name on; i ends at its '>'.
    private static Open StartTag(byte[] text, int begin, ref int i, int utf16Offset, int resultPosition)
    {
        while (!Markup.IsSpace(At(text, i)) && At(text, i) != '>')
        {
            i++;
        }

        if (At(text, i) == 0)
        {
            throw new FormattingException($"Unclosed start tag at byte offset {begin}");
        }

        var tag = Markup.Lower(text, begin + 1, i - begin - 1);
        if (!SupportedTags.Contains(tag))
        {
            throw new FormattingException($"Unsupported start tag \"{tag}\" at byte offset {begin}");
        }

        var argument = "";
        while (At(text, i) != '>')
        {
            while (At(text, i) != 0 && Markup.IsSpace(At(text, i)))
            {
                i++;
            }

            if (At(text, i) == '>')
            {
                break;
            }

            var attributeBegin = i;
            while (
                !Markup.IsSpace(At(text, i))
                && At(text, i) is not ((byte)'=' or (byte)'>' or (byte)'/' or (byte)'"' or (byte)'\'')
            )
            {
                i++;
            }

            var attribute = Encoding.UTF8.GetString(text, attributeBegin, i - attributeBegin);
            if (attribute.Length == 0)
            {
                throw new FormattingException($"Empty attribute name in the tag \"{tag}\" at byte offset {begin}");
            }

            while (At(text, i) != 0 && Markup.IsSpace(At(text, i)))
            {
                i++;
            }

            if (At(text, i) != '=')
            {
                if (At(text, i) == 0)
                {
                    throw new FormattingException($"Unclosed start tag \"{tag}\" at byte offset {begin}");
                }

                if (tag == "blockquote" && attribute == "expandable")
                {
                    argument = "1";
                }

                continue;
            }

            i++;
            while (At(text, i) != 0 && Markup.IsSpace(At(text, i)))
            {
                i++;
            }

            if (At(text, i) == 0)
            {
                throw new FormattingException($"Unclosed start tag \"{tag}\" at byte offset {begin}");
            }

            var value = AttributeValue(text, ref i);
            if (At(text, i) == 0)
            {
                throw new FormattingException($"Unclosed start tag at byte offset {begin}");
            }

            // tg-time's unix and format are cut.
            argument = (tag, attribute) switch
            {
                ("a", "href") or ("tg-emoji", "emoji-id") => value,
                ("code", "class") when value.StartsWith("language-", StringComparison.Ordinal) => value[9..],
                ("span", "class") when value.StartsWith("tg-", StringComparison.Ordinal) => value[3..],
                ("blockquote", "expandable") => "1",
                _ => argument,
            };
        }

        if (tag == "span" && argument != "spoiler")
        {
            throw new FormattingException($"Tag \"span\" must have class \"tg-spoiler\" at byte offset {begin}");
        }

        return new Open(tag, argument, utf16Offset, resultPosition);
    }

    // A name token, lowercased, or a quoted string with its references decoded.
    private static string AttributeValue(byte[] text, ref int i)
    {
        if (At(text, i) is not ((byte)'\'' or (byte)'"'))
        {
            var tokenBegin = i;
            while (Markup.IsAlnum(At(text, i)) || At(text, i) is (byte)'.' or (byte)'-')
            {
                i++;
            }

            if (!Markup.IsSpace(At(text, i)) && At(text, i) != '>')
            {
                throw new FormattingException($"Unexpected end of name token at byte offset {tokenBegin}");
            }

            return Markup.Lower(text, tokenBegin, i - tokenBegin);
        }

        var end = At(text, i++);
        var value = new List<byte>();
        while (At(text, i) != end && At(text, i) != 0)
        {
            if (At(text, i) == '&')
            {
                var position = i;
                var code = DecodeEntity(text, ref position);
                if (code != 0)
                {
                    Markup.AppendUtf8(value, code);
                    i = position;
                    continue;
                }
            }

            value.Add(At(text, i++));
        }

        if (At(text, i) == end)
        {
            i++;
        }

        return Encoding.UTF8.GetString([.. value]);
    }

    private static void AddEntity(List<JsonObject> entities, Open open, int length, List<byte> result)
    {
        var offset = open.Offset;
        var last = entities.Count == 0 ? null : entities[^1];
        bool SameSpan(JsonObject? entity) =>
            entity?["offset"]!.GetValue<int>() == offset && entity["length"]!.GetValue<int>() == length;

        switch (open.Tag)
        {
            case "b" or "strong":
                entities.Add(Markup.Entity("bold", offset, length));
                break;
            case "i" or "em":
                entities.Add(Markup.Entity("italic", offset, length));
                break;
            case "u" or "ins":
                entities.Add(Markup.Entity("underline", offset, length));
                break;
            case "s" or "strike" or "del":
                entities.Add(Markup.Entity("strikethrough", offset, length));
                break;
            case "tg-spoiler" or "span":
                entities.Add(Markup.Entity("spoiler", offset, length));
                break;
            case "tg-emoji":
                if (
                    !long.TryParse(open.Argument, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    || id <= 0
                )
                {
                    throw new FormattingException("Invalid custom emoji identifier specified");
                }

                var emoji = Markup.Entity("custom_emoji", offset, length);
                emoji["custom_emoji_id"] = open.Argument;
                entities.Add(emoji);
                break;
            case "a":
                // The link is kept as given: Telegram's checks and tg://user links are cut.
                var url =
                    open.Argument.Length > 0
                        ? open.Argument
                        : Encoding.UTF8.GetString([.. result.Skip(open.ResultPosition)]);
                if (url.Length > 0)
                {
                    var link = Markup.Entity("text_link", offset, length);
                    link["url"] = url;
                    entities.Add(link);
                }

                break;
            case "pre":
                if (last?["type"]!.GetValue<string>() == "code" && SameSpan(last) && last!.ContainsKey("language"))
                {
                    last["type"] = "pre";
                }
                else
                {
                    entities.Add(Markup.Entity("pre", offset, length));
                }

                break;
            case "code":
                if (last?["type"]!.GetValue<string>() == "pre" && SameSpan(last) && open.Argument.Length > 0)
                {
                    last!["language"] = open.Argument;
                }
                else
                {
                    var code = Markup.Entity("code", offset, length);
                    if (open.Argument.Length > 0)
                    {
                        code["language"] = open.Argument;
                    }

                    entities.Add(code);
                }

                break;
            case "blockquote":
                entities.Add(
                    Markup.Entity(open.Argument.Length > 0 ? "expandable_blockquote" : "blockquote", offset, length)
                );
                break;

            // tg-time makes no entity: it's cut.
        }
    }

    // TDLib's decode_html_entity: &lt; &gt; &amp; &quot; and numeric references, with or without ';'; 0 for anything
    // else, which stays literal. position ends after the reference.
    private static uint DecodeEntity(byte[] text, ref int position)
    {
        var end = position + 1;
        uint code = 0;
        if (At(text, position + 1) == '#')
        {
            end++;
            if (At(text, position + 2) == 'x')
            {
                end++;
                while (Markup.IsHexDigit(At(text, end)))
                {
                    code = unchecked(code * 16 + (uint)Markup.HexValue(At(text, end++)));
                }
            }
            else
            {
                while (Markup.IsDigit(At(text, end)))
                {
                    code = unchecked(code * 10 + (uint)(At(text, end++) - '0'));
                }
            }

            if (code == 0 || code >= 0x10FFFF || end - position >= 10)
            {
                return 0;
            }
        }
        else
        {
            while (Markup.IsAlpha(At(text, end)))
            {
                end++;
            }

            code = Encoding.ASCII.GetString(text, position + 1, end - position - 1) switch
            {
                "lt" => '<',
                "gt" => '>',
                "amp" => '&',
                "quot" => '"',
                _ => 0,
            };

            if (code == 0)
            {
                return 0;
            }
        }

        position = At(text, end) == ';' ? end + 1 : end;
        return code;
    }

    // Past the end reads as NUL, as the C string TDLib reads.
    private static byte At(byte[] text, int index) => index < text.Length ? text[index] : (byte)0;

    private sealed record Open(string Tag, string Argument, int Offset, int ResultPosition);
}
