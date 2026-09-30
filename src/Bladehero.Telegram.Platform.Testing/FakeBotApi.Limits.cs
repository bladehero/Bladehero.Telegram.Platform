using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// What Telegram accepts from the bot: answers within their limit, and inline keyboards whose buttons each do something.
public sealed partial class FakeBotApi
{
    internal const int TextLimit = 4096;
    internal const int CaptionLimit = 1024;

    private const int CallbackDataLimit = 64;
    private const int AnswerTextLimit = 200;

    private static void ThrowIfLongerThan(int limit, string? value, string description)
    {
        if (value?.Length > limit)
        {
            throw Refuse(400, description);
        }
    }

    // Only an inline keyboard is attached to a message; a reply keyboard lives on the user's side. One without buttons
    // counts as none.
    private static JsonNode? InlineKeyboardOf(JsonObject parameters)
    {
        if (parameters["reply_markup"] is not JsonObject markup || markup["inline_keyboard"] is not JsonArray rows)
        {
            return null;
        }

        var buttons = rows.OfType<JsonArray>().SelectMany(row => row.OfType<JsonObject>()).ToArray();
        foreach (var button in buttons)
        {
            if (button["callback_data"]?.GetValue<string>() is { } data)
            {
                if (data.Length == 0 || Encoding.UTF8.GetByteCount(data) > CallbackDataLimit)
                {
                    throw Refuse(400, "Bad Request: BUTTON_DATA_INVALID");
                }
            }
            else if (button.All(field => field.Key == "text"))
            {
                throw Refuse(400, "Bad Request: text buttons are unallowed in the inline keyboard");
            }
        }

        return buttons.Length > 0 ? markup.DeepClone() : null;
    }
}
