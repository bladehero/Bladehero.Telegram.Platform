using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Builds inline keyboards from <see cref="ButtonAttribute"/> structs.</summary>
public static class InlineKeyboardMarkupExtensions
{
    /// <summary>
    /// Adds a button labelled <paramref name="text"/> carrying <paramref name="button"/>'s data to the last row and
    /// returns the keyboard, as Telegram.Bot's <c>AddButton(text, callbackData)</c> does; <c>AddNewRow()</c> starts the
    /// next row.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="ButtonData.Encode{TButton}"/>.</exception>
    /// <exception cref="InvalidOperationException">As <see cref="ButtonData.Encode{TButton}"/>.</exception>
    public static InlineKeyboardMarkup AddButton<TButton>(
        this InlineKeyboardMarkup keyboard,
        string text,
        TButton button
    )
        where TButton : struct => keyboard.AddButton(text, ButtonData.Encode(button));
}
