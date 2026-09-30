using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// What Telegram accepts from the bot: answers within their limit, and inline keyboards whose buttons each do something.
public sealed partial class FakeBotApi
{
    internal const int TextLimit = 4096;
    internal const int CaptionLimit = 1024;

    private const int CallbackDataLimit = 64;
    private const int ButtonsPerRow = 12;
    private const int KeyboardButtonLimit = 300;
    private const int AnswerTextLimit = 200;

    private static void ThrowIfLongerThan(int limit, string? value, string description)
    {
        if (value?.Length > limit)
        {
            throw Refuse(400, description);
        }
    }

    // Only an inline keyboard is attached to a message; a reply keyboard lives on the user's side. As Telegram does,
    // buttons without text are dropped, then empty rows, and rows and the keyboard are cut to their limits; one left
    // without buttons counts as none.
    private static JsonNode? InlineKeyboardOf(JsonObject parameters)
    {
        if (parameters["reply_markup"] is not JsonObject markup || markup["inline_keyboard"] is not JsonArray rows)
        {
            return null;
        }

        var kept = new JsonArray();
        var count = 0;
        foreach (var row in rows.OfType<JsonArray>())
        {
            var buttons = row.OfType<JsonObject>()
                .Where(button => button["text"]?.GetValue<string>() is { Length: > 0 })
                .Take(Math.Min(ButtonsPerRow, KeyboardButtonLimit - count))
                .Select(Checked)
                .ToArray();

            if (buttons.Length > 0)
            {
                kept.Add(new JsonArray(buttons));
                count += buttons.Length;
            }
        }

        if (count == 0)
        {
            return null;
        }

        var keyboard = markup.DeepClone().AsObject();
        keyboard["inline_keyboard"] = kept;
        return keyboard;

        // Empty callback data counts as none.
        static JsonNode Checked(JsonObject button)
        {
            var copy = button.DeepClone().AsObject();
            if (copy["callback_data"]?.GetValue<string>() is "")
            {
                copy.Remove("callback_data");
            }

            if (
                copy["callback_data"]?.GetValue<string>() is { } data
                && Encoding.UTF8.GetByteCount(data) > CallbackDataLimit
            )
            {
                throw Refuse(400, "Bad Request: BUTTON_DATA_INVALID");
            }

            if (copy.All(field => field.Key == "text"))
            {
                throw Refuse(400, "Bad Request: text buttons are not allowed in the inline keyboard");
            }

            return copy;
        }
    }
}
