namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

// Where Telegram marks bot_command entities, as tdlib's match_bot_commands finds them: a / and 1-64 of [A-Za-z0-9_],
// then optionally @ and a name of 3-32, with neither a word character, /, < nor > on either side.
internal static class BotCommands
{
    private const int LongestName = 64;
    private const int ShortestUsername = 3;
    private const int LongestUsername = 32;

    // The offset and length of every command in the text.
    public static IEnumerable<(int Offset, int Length)> Find(string text)
    {
        for (var slash = text.IndexOf('/'); slash >= 0; slash = text.IndexOf('/', slash + 1))
        {
            if (LengthAt(text, slash) is { } length)
            {
                yield return (slash, length);
            }
        }
    }

    // The length of the command the text starts with, or null.
    public static int? LengthAtStart(string text) => text.StartsWith('/') ? LengthAt(text, 0) : null;

    private static int? LengthAt(string text, int slash)
    {
        if (slash > 0 && Blocks(text[slash - 1]))
        {
            return null;
        }

        var end = EndOfName(text, slash + 1);
        if (end - slash - 1 is 0 or > LongestName)
        {
            return null;
        }

        if (end < text.Length && text[end] == '@')
        {
            var usernameEnd = EndOfName(text, end + 1);
            if (usernameEnd - end - 1 is < ShortestUsername or > LongestUsername)
            {
                return null;
            }

            end = usernameEnd;
        }

        return end < text.Length && Blocks(text[end]) ? null : end - slash;
    }

    private static int EndOfName(string text, int from)
    {
        while (from < text.Length && (char.IsAsciiLetterOrDigit(text[from]) || text[from] == '_'))
        {
            from++;
        }

        return from;
    }

    private static bool Blocks(char neighbour) =>
        char.IsLetterOrDigit(neighbour) || neighbour is '_' or '/' or '<' or '>';
}
