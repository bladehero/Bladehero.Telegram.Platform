using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Writes and reads the callback data of <see cref="ButtonAttribute"/> structs.</summary>
/// <remarks>Values are written in one canonical, invariant form, and only that form decodes.</remarks>
public static class ButtonData
{
    /// <summary>The most bytes of callback data Telegram takes.</summary>
    public const int MaxBytes = 64;

    /// <summary>The callback data of <paramref name="button"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The data is over <see cref="MaxBytes"/> bytes, or a value can't be written: a <c>null</c> string, a lone
    /// surrogate, or an enum value without a name.
    /// </exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TButton"/> can't be button data.</exception>
    public static string Encode<TButton>(TButton button)
        where TButton : struct => ButtonCodec.Of(typeof(TButton)).Encode(button);

    /// <summary>
    /// Reads <paramref name="data"/> as a <typeparamref name="TButton"/>; <c>false</c>, with <c>default</c>, for
    /// <c>null</c> or any other data.
    /// </summary>
    /// <remarks>Missing trailing fields take their constructor defaults, so older buttons keep decoding.</remarks>
    /// <exception cref="InvalidOperationException"><typeparamref name="TButton"/> can't be button data.</exception>
    public static bool TryDecode<TButton>(string? data, out TButton button)
        where TButton : struct
    {
        var codec = ButtonCodec.Of(typeof(TButton));
        if (data is not null && codec.TryDecode(data, out var decoded))
        {
            button = (TButton)decoded!;
            return true;
        }

        button = default;
        return false;
    }

    /// <summary>A callback button labelled <paramref name="text"/> that carries <paramref name="button"/>.</summary>
    /// <exception cref="ArgumentException">As <see cref="Encode{TButton}"/>.</exception>
    /// <exception cref="InvalidOperationException">As <see cref="Encode{TButton}"/>.</exception>
    public static InlineKeyboardButton Button<TButton>(string text, TButton button)
        where TButton : struct => InlineKeyboardButton.WithCallbackData(text, Encode(button));
}
