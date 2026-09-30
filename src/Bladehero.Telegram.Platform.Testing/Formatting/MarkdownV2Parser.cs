using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Testing.Formatting;

// TDLib's parse_markdown_v2: plain text and entities from MarkdownV2, with its error messages and byte offsets.
internal static partial class MarkdownV2Parser
{
    private const string Reserved = "_*[]()~`>#+-=|{}.!\n";

    private enum Kind
    {
        Bold,
        Italic,
        Underline,
        Strikethrough,
        Spoiler,
        Code,
        Pre,
        PreCode,
        TextUrl,
        CustomEmoji,
        BlockQuote,
        ExpandableBlockQuote,
    }

    public static (string Text, List<JsonObject> Entities) Parse(string input)
    {
        var text = Encoding.UTF8.GetBytes(input);
        var result = new List<byte>(text.Length);
        var entities = new List<JsonObject>();
        var nested = new List<Open>();
        var utf16Offset = 0;
        var haveBlockquote = false;
        var canStartBlockquote = true;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && At(text, i + 1) is > 0 and <= 126)
            {
                i++;
                utf16Offset++;
                result.Add(text[i]);
                if (text[i] != '\r')
                {
                    canStartBlockquote = text[i] == '\n';
                }

                continue;
            }

            var reserved =
                nested.Count > 0 && nested[^1].Kind is Kind.Code or Kind.Pre or Kind.PreCode ? "`" : Reserved;
            if (c >= 0x80 || !reserved.Contains((char)c))
            {
                if (Markup.Utf16Units(c) > 0)
                {
                    utf16Offset += Markup.Utf16Units(c);
                    if (c != '\r')
                    {
                        canStartBlockquote = false;
                    }
                }

                result.Add(c);
                continue;
            }

            if (!EndsAnEntity(text, i, nested, haveBlockquote))
            {
                // The beginning of an entity.
                var byteOffset = i;
                var argument = "";
                Kind? kind;
                switch ((char)c)
                {
                    case '_' when At(text, i + 1) == '_':
                        kind = Kind.Underline;
                        i++;
                        break;
                    case '_':
                        kind = Kind.Italic;
                        break;
                    case '*':
                        kind = Kind.Bold;
                        break;
                    case '~':
                        kind = Kind.Strikethrough;
                        break;
                    case '|' when At(text, i + 1) == '|':
                        i++;
                        kind = Kind.Spoiler;
                        break;
                    case '[':
                        kind = Kind.TextUrl;
                        break;
                    case '`' when At(text, i + 1) == '`' && At(text, i + 2) == '`':
                        i += 3;
                        kind = Kind.Pre;
                        var languageEnd = i;
                        while (!Markup.IsSpace(At(text, languageEnd)) && At(text, languageEnd) != '`')
                        {
                            languageEnd++;
                        }

                        if (i != languageEnd && languageEnd < text.Length && At(text, languageEnd) != '`')
                        {
                            kind = Kind.PreCode;
                            argument = Encoding.UTF8.GetString(text, i, languageEnd - i);
                            i = languageEnd;
                        }

                        // One new line at the beginning is skipped.
                        if (At(text, i) is (byte)'\n' or (byte)'\r')
                        {
                            i += At(text, i + 1) is (byte)'\n' or (byte)'\r' && At(text, i) != At(text, i + 1) ? 2 : 1;
                        }

                        i--;
                        break;
                    case '`':
                        kind = Kind.Code;
                        break;
                    case '!' when At(text, i + 1) == '[':
                        i++;
                        kind = Kind.CustomEmoji;
                        break;
                    case '\n':
                        utf16Offset++;
                        result.Add((byte)'\n');
                        canStartBlockquote = true;
                        kind = null;
                        break;
                    case '>' when canStartBlockquote:
                        kind = haveBlockquote ? null : Kind.BlockQuote;
                        haveBlockquote = true;
                        break;
                    default:
                        throw new FormattingException(
                            $"Character '{(char)c}' is reserved and must be escaped with the preceding '\\'"
                        );
                }

                if (kind is { } opened)
                {
                    nested.Add(new Open(opened, argument, utf16Offset, byteOffset, result.Count));
                }

                continue;
            }

            // The end of an entity.
            var type = nested[^1].Kind;
            if (c == '\n' && type != Kind.BlockQuote)
            {
                // Only "||" right before a new line in a blockquote, which makes it expandable, ends here.
                var spoilerAtLineEnd =
                    type == Kind.Spoiler
                    && (
                        nested[^1].ByteOffset == i - 2
                        || nested[^1].ByteOffset == i - 3 && result.Count != 0 && result[^1] == '\r'
                    );
                if (!spoilerAtLineEnd)
                {
                    throw MissingEnd(nested[^1]);
                }

                nested.RemoveAt(nested.Count - 1);
                if (nested[^1].Kind != Kind.BlockQuote)
                {
                    throw MissingEnd(nested[^1]);
                }

                type = Kind.ExpandableBlockQuote;
            }

            var open = nested[^1];
            var entityArgument = open.Argument;
            string? customEmojiId = null;
            var skip = utf16Offset == open.Offset;
            switch (type)
            {
                case Kind.Underline or Kind.Spoiler:
                    i++;
                    break;
                case Kind.Pre or Kind.PreCode:
                    i += 2;
                    break;
                case Kind.TextUrl:
                    // The link is kept as given: Telegram's checks and tg://user links are cut.
                    string url;
                    if (At(text, i + 1) != '(')
                    {
                        url = Encoding.UTF8.GetString([.. result.Skip(open.ResultPosition)]);
                    }
                    else
                    {
                        i += 2;
                        url = UrlUntilParenthesis(text, ref i);
                    }

                    skip |= url.Length == 0;
                    entityArgument = url;
                    break;
                case Kind.CustomEmoji:
                    if (At(text, i + 1) != '(')
                    {
                        throw new FormattingException("The entity must contain a tg://emoji or tg://time URL");
                    }

                    i += 2;
                    var link = UrlUntilParenthesis(text, ref i);
                    if (
                        EmojiLink().Match(link) is { Success: true } emoji
                        && long.TryParse(emoji.Groups[1].Value, out var id)
                        && id > 0
                    )
                    {
                        customEmojiId = emoji.Groups[1].Value;
                    }
                    else if (TimeLink().IsMatch(link))
                    {
                        // A tg://time entity is cut.
                        skip = true;
                    }
                    else
                    {
                        throw new FormattingException("Invalid tg://emoji or tg://time URL specified");
                    }

                    break;
                case Kind.BlockQuote or Kind.ExpandableBlockQuote:
                    haveBlockquote = false;
                    result.Add(text[i]);
                    canStartBlockquote = true;
                    utf16Offset++;
                    skip = false;
                    break;
            }

            if (!skip)
            {
                entities.Add(EntityOf(type, open.Offset, utf16Offset - open.Offset, entityArgument, customEmojiId));
            }

            nested.RemoveAt(nested.Count - 1);
        }

        if (haveBlockquote)
        {
            var type = Kind.BlockQuote;
            if (nested[^1].Kind == Kind.Spoiler && nested[^1].ByteOffset == text.Length - 2)
            {
                nested.RemoveAt(nested.Count - 1);
                type = Kind.ExpandableBlockQuote;
            }

            if (nested[^1].Kind == Kind.BlockQuote)
            {
                var open = nested[^1];
                if (utf16Offset != open.Offset)
                {
                    entities.Add(EntityOf(type, open.Offset, utf16Offset - open.Offset, "", null));
                }

                nested.RemoveAt(nested.Count - 1);
            }
        }

        if (nested.Count != 0)
        {
            throw MissingEnd(nested[^1]);
        }

        return (Encoding.UTF8.GetString([.. result]), Markup.Sorted(entities));
    }

    private static bool EndsAnEntity(byte[] text, int i, List<Open> nested, bool haveBlockquote)
    {
        if (nested.Count == 0)
        {
            return false;
        }

        var c = text[i];
        if (haveBlockquote && c == '\n' && (i + 1 == text.Length || text[i + 1] != '>'))
        {
            return true;
        }

        return nested[^1].Kind switch
        {
            Kind.Bold => c == '*',
            Kind.Italic => c == '_' && At(text, i + 1) != '_',
            Kind.Code => c == '`',
            Kind.Pre or Kind.PreCode => c == '`' && At(text, i + 1) == '`' && At(text, i + 2) == '`',
            Kind.TextUrl or Kind.CustomEmoji => c == ']',
            Kind.Underline => c == '_' && At(text, i + 1) == '_',
            Kind.Strikethrough => c == '~',
            Kind.Spoiler => c == '|' && At(text, i + 1) == '|',
            _ => false,
        };
    }

    // From after "(" to the ")", with \-escapes; i ends at the ")".
    private static string UrlUntilParenthesis(byte[] text, ref int i)
    {
        var begin = i;
        var url = new List<byte>();
        while (i < text.Length && text[i] != ')')
        {
            if (text[i] == '\\' && At(text, i + 1) is > 0 and <= 126)
            {
                url.Add(text[i + 1]);
                i += 2;
                continue;
            }

            url.Add(text[i++]);
        }

        if (At(text, i) != ')')
        {
            throw new FormattingException($"Can't find end of a URL at byte offset {begin}");
        }

        return Encoding.UTF8.GetString([.. url]);
    }

    private static JsonObject EntityOf(Kind kind, int offset, int length, string argument, string? customEmojiId)
    {
        var entity = Markup.Entity(
            kind switch
            {
                Kind.Bold => "bold",
                Kind.Italic => "italic",
                Kind.Underline => "underline",
                Kind.Strikethrough => "strikethrough",
                Kind.Spoiler => "spoiler",
                Kind.Code => "code",
                Kind.Pre or Kind.PreCode => "pre",
                Kind.TextUrl => "text_link",
                Kind.CustomEmoji => "custom_emoji",
                Kind.BlockQuote => "blockquote",
                _ => "expandable_blockquote",
            },
            offset,
            length
        );

        switch (kind)
        {
            case Kind.PreCode:
                entity["language"] = argument;
                break;
            case Kind.TextUrl:
                entity["url"] = argument;
                break;
            case Kind.CustomEmoji:
                entity["custom_emoji_id"] = customEmojiId;
                break;
        }

        return entity;
    }

    private static FormattingException MissingEnd(Open open) =>
        new($"Can't find end of {open.Kind} entity at byte offset {open.ByteOffset}");

    private static byte At(byte[] text, int index) => index < text.Length ? text[index] : (byte)0;

    [GeneratedRegex(@"^tg://emoji\?id=([0-9]{1,19})$", RegexOptions.IgnoreCase)]
    private static partial Regex EmojiLink();

    [GeneratedRegex(@"^tg://time\?unix=-?[0-9]+(&.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeLink();

    private sealed record Open(Kind Kind, string Argument, int Offset, int ByteOffset, int ResultPosition);
}
