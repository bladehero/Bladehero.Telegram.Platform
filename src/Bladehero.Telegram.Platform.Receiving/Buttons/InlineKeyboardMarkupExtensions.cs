using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Builds inline keyboards from <see cref="ButtonDataAttribute"/> structs.</summary>
public static class InlineKeyboardMarkupExtensions
{
    /// <summary>
    /// Adds a button carrying <paramref name="button"/> to the last row, as Telegram.Bot's
    /// <c>AddButton(text, callbackData)</c> does.
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
