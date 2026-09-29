using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Writes and reads the callback data of <see cref="ButtonAttribute"/> structs.</summary>
/// <remarks>
/// The data is the prefix, then <c>:</c> and each field in the invariant culture: integers as digits, bool as
/// <c>1</c>/<c>0</c>, a <see cref="Guid"/> in 32 lower-case hex digits, an enum by its lower-case name, a
/// <see cref="DateOnly"/> as <c>yyyy-MM-dd</c>, a string with <c>%</c>, <c>:</c> and <c>@</c> escaped as <c>%25</c>,
/// <c>%3A</c> and <c>%40</c>, and a nullable <c>null</c> as an empty segment. Only that form decodes.
/// </remarks>
public static class ButtonData
{
    /// <summary>The most bytes of callback data Telegram takes.</summary>
    public const int MaxBytes = 64;

    /// <summary>The callback data of <paramref name="button"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The data is over <see cref="MaxBytes"/> bytes, or a field can't be written: a <c>null</c> string, one with a
    /// lone surrogate, or an enum value without a name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TButton"/> isn't button data: it has no <see cref="ButtonAttribute"/>, or its fields aren't
    /// ones a button can hold.
    /// </exception>
    public static string Encode<TButton>(TButton button)
        where TButton : struct => ButtonCodec.Of(typeof(TButton)).Encode(button);

    /// <summary>
    /// Reads <paramref name="data"/> as a <typeparamref name="TButton"/>; <c>false</c>, with <c>default</c>, for
    /// <c>null</c>, another button's data, or data that isn't in the form <see cref="Encode{TButton}"/> writes.
    /// </summary>
    /// <remarks>
    /// Data missing trailing fields whose constructor parameters have defaults decodes with those defaults, so a field
    /// added at the end with a default keeps older buttons working.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TButton"/> isn't button data: it has no <see cref="ButtonAttribute"/>, or its fields aren't
    /// ones a button can hold.
    /// </exception>
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
