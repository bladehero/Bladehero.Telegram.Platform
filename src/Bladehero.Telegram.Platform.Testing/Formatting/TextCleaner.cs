using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing.Formatting;

// TDLib's clean_input_string and fix_formatted_text: text as Telegram keeps it, entities moved and clipped along.
internal static class TextCleaner
{
    // Control characters but \n become spaces; \r, U+2028-U+202E and the combining vertical lines go.
    public static (string Text, List<JsonObject> Entities) Clean(string text, IEnumerable<JsonObject> entities)
    {
        // Where each UTF-16 position of the input lands in the output.
        var map = new int[text.Length + 1];
        var cleaned = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            map[i] = cleaned.Length;
            var c = text[i];
            if (c is '\r' or (>= (char)0x2028 and <= (char)0x202E) or (char)0x0333 or (char)0x033F or (char)0x030A)
            {
                continue;
            }

            cleaned.Append(c < ' ' && c != '\n' ? ' ' : c);
        }

        map[text.Length] = cleaned.Length;

        var moved = new List<JsonObject>();
        foreach (var entity in entities)
        {
            var offset = Math.Clamp(entity["offset"]!.GetValue<int>(), 0, text.Length);
            var end = Math.Clamp(offset + entity["length"]!.GetValue<int>(), offset, text.Length);
            var copy = entity.DeepClone().AsObject();
            copy["offset"] = map[offset];
            copy["length"] = map[end] - map[offset];
            moved.Add(copy);
        }

        return (cleaned.ToString(), NonEmpty(moved));
    }

    // Removes ' ' and '\n' at the end, clipping entities, and at the start up to the first entity; NBSP and U+3000
    // stay.
    public static (string Text, List<JsonObject> Entities) Trim(string text, List<JsonObject> entities)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is ' ' or '\n')
        {
            end--;
        }

        var kept = new List<JsonObject>();
        foreach (var entity in Markup.Sorted(entities))
        {
            var offset = entity["offset"]!.GetValue<int>();
            if (offset < end)
            {
                entity["length"] = Math.Min(entity["length"]!.GetValue<int>(), end - offset);
                kept.Add(entity);
            }
        }

        var firstEntity = kept.Count == 0 ? end : kept[0]["offset"]!.GetValue<int>();
        var start = 0;
        while (start < firstEntity && start < end && text[start] is ' ' or '\n')
        {
            start++;
        }

        foreach (var entity in kept)
        {
            entity["offset"] = entity["offset"]!.GetValue<int>() - start;
        }

        return (text[start..end], Markup.Sorted(NonEmpty(kept)));
    }

    // TDLib's is_empty_string (strip_empty_characters): nothing but spaces, new lines and invisible characters.
    public static bool IsEmptyString(string text) => text.EnumerateRunes().All(rune => IsEmptyCharacter(rune.Value));

    private static bool IsEmptyCharacter(int c) =>
        c
            is ' '
                or '\n'
                or '\t'
                or '\r'
                or '\v'
                or '\0'
                // Turned into spaces.
                or 0x1680
                or 0x180E
                or (>= 0x2000 and <= 0x200A)
                or 0x202E
                or 0x202F
                or 0x205F
                or 0x2800
                or 0x3000
                or 0xFFFC
                or (>= 0xE0000 and <= 0xE007F)
                // Counted as empty.
                or (>= 0x200B and <= 0x200F)
                or 0xFEFF
                or 0x00A0;

    private static List<JsonObject> NonEmpty(List<JsonObject> entities) =>
        [.. entities.Where(entity => entity["length"]!.GetValue<int>() > 0)];
}
