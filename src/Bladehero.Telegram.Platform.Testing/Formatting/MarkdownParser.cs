using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing.Formatting;

// TDLib's parse_markdown for legacy Markdown: *bold*, _italic_, `code`, ```pre``` and [text](url).
internal static class MarkdownParser
{
    public static (string Text, List<JsonObject> Entities) Parse(string input)
    {
        var text = Encoding.UTF8.GetBytes(input);
        var result = new List<byte>(text.Length);
        var entities = new List<JsonObject>();
        var utf16Offset = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && At(text, i + 1) is (byte)'_' or (byte)'*' or (byte)'`' or (byte)'[')
            {
                i++;
                result.Add(text[i]);
                utf16Offset++;
                continue;
            }

            if (c is not ((byte)'_' or (byte)'*' or (byte)'`' or (byte)'['))
            {
                utf16Offset += Markup.Utf16Units(c);
                result.Add(c);
                continue;
            }

            // The beginning of an entity.
            var begin = i;
            var end = c == '[' ? (byte)']' : c;
            var isPre = false;
            var language = "";
            i++;

            if (c == '`' && At(text, i) == '`' && At(text, i + 1) == '`')
            {
                i += 2;
                isPre = true;
                var languageEnd = i;
                while (!Markup.IsSpace(At(text, languageEnd)) && At(text, languageEnd) != '`')
                {
                    languageEnd++;
                }

                if (i != languageEnd && languageEnd < text.Length && At(text, languageEnd) != '`')
                {
                    language = Encoding.UTF8.GetString(text, i, languageEnd - i);
                    i = languageEnd;
                }

                // One new line at the beginning is skipped.
                if (At(text, i) is (byte)'\n' or (byte)'\r')
                {
                    i += At(text, i + 1) is (byte)'\n' or (byte)'\r' && At(text, i) != At(text, i + 1) ? 2 : 1;
                }
            }

            var entityOffset = utf16Offset;
            var contentBegin = result.Count;
            while (i < text.Length && (text[i] != end || isPre && !(At(text, i + 1) == '`' && At(text, i + 2) == '`')))
            {
                utf16Offset += Markup.Utf16Units(text[i]);
                result.Add(text[i++]);
            }

            if (i == text.Length)
            {
                throw new FormattingException($"Can't find end of the entity starting at byte offset {begin}");
            }

            if (entityOffset != utf16Offset)
            {
                var length = utf16Offset - entityOffset;
                switch ((char)c)
                {
                    case '_':
                        entities.Add(Markup.Entity("italic", entityOffset, length));
                        break;
                    case '*':
                        entities.Add(Markup.Entity("bold", entityOffset, length));
                        break;
                    case '[':
                        // The link text serves as the URL when none is given; Telegram's checks are cut.
                        string url;
                        if (At(text, i + 1) != '(')
                        {
                            url = Encoding.UTF8.GetString([.. result.Skip(contentBegin)]);
                        }
                        else
                        {
                            i += 2;
                            var urlBytes = new List<byte>();
                            while (i < text.Length && text[i] != ')')
                            {
                                urlBytes.Add(text[i++]);
                            }

                            url = Encoding.UTF8.GetString([.. urlBytes]);
                        }

                        if (url.Length > 0)
                        {
                            var link = Markup.Entity("text_link", entityOffset, length);
                            link["url"] = url;
                            entities.Add(link);
                        }

                        break;
                    case '`' when isPre:
                        var pre = Markup.Entity("pre", entityOffset, length);
                        if (language.Length > 0)
                        {
                            pre["language"] = language;
                        }

                        entities.Add(pre);
                        break;
                    case '`':
                        entities.Add(Markup.Entity("code", entityOffset, length));
                        break;
                }
            }

            if (isPre)
            {
                i += 2;
            }
        }

        return (Encoding.UTF8.GetString([.. result]), entities);
    }

    private static byte At(byte[] text, int index) => index < text.Length ? text[index] : (byte)0;
}
