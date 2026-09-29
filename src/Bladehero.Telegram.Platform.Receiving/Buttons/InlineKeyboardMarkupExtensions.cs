using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Builds inline keyboards from <see cref="ButtonDataAttribute"/> structs.</summary>
public static class InlineKeyboardMarkupExtensions
{
    /// <summary>
    /// Adds a button carrying <paramref name="button"/> to the last row, as Telegram.Bot's
    /// <c>AddButton(text, callbackData)</c> does.
    /// </summary>
    /// <exception cref="ArgumentException">As <see cref="ButtonData.Encode{TButton}(TButton)"/>.</exception>
    /// <exception cref="InvalidOperationException">As <see cref="ButtonData.Encode{TButton}(TButton)"/>.</exception>
    public static InlineKeyboardMarkup AddButton<TButton>(
        this InlineKeyboardMarkup keyboard,
        string text,
        TButton button
    )
        where TButton : struct => keyboard.AddButton(text, ButtonData.Encode(button));

    /// <summary>
    /// Adds a button carrying <paramref name="button"/>'s data bound to a conversation run, from <c>BindAsync</c>, to
    /// the last row and returns the keyboard: a tap by anyone else, or from an ended or replaced run, is answered
    /// before any step runs.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// As <see cref="ButtonData.Encode{TButton}(TButton, ConversationBinding)"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">As <see cref="ButtonData.Encode{TButton}(TButton)"/>.</exception>
    public static InlineKeyboardMarkup AddButton<TButton>(
        this InlineKeyboardMarkup keyboard,
        string text,
        TButton button,
        ConversationBinding binding
    )
        where TButton : struct => keyboard.AddButton(text, ButtonData.Encode(button, binding));
}
