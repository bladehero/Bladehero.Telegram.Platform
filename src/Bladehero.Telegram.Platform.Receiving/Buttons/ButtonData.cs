using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Writes and reads the callback data of <see cref="ButtonDataAttribute"/> structs.</summary>
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
    /// The callback data of <paramref name="button"/>, bound to a conversation run from <c>BindAsync</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// As <see cref="Encode{TButton}(TButton)"/>, with the binding counted in the 64 bytes, or the binding's
    /// conversation id isn't 1-16 characters of a-z and 0-9.
    /// </exception>
    /// <exception cref="InvalidOperationException">As <see cref="Encode{TButton}(TButton)"/>.</exception>
    public static string Encode<TButton>(TButton button, ConversationBinding binding)
        where TButton : struct => ButtonCodec.Of(typeof(TButton)).Encode(button, binding);

    /// <summary>
    /// Reads <paramref name="data"/> as a <typeparamref name="TButton"/>, bound or not; <c>false</c>, with
    /// <c>default</c>, for <c>null</c> or any other data.
    /// </summary>
    /// <remarks>Missing trailing fields take their constructor defaults, so older buttons keep decoding.</remarks>
    /// <exception cref="InvalidOperationException"><typeparamref name="TButton"/> can't be button data.</exception>
    public static bool TryDecode<TButton>(string? data, out TButton button)
        where TButton : struct => TryDecode(data, out button, out _);

    /// <summary>
    /// Decodes data with or without a binding; <paramref name="binding"/> is <c>null</c> for an unbound button.
    /// </summary>
    /// <remarks>As <see cref="TryDecode{TButton}(string, out TButton)"/>.</remarks>
    /// <exception cref="InvalidOperationException">
    /// As <see cref="TryDecode{TButton}(string, out TButton)"/>.
    /// </exception>
    public static bool TryDecode<TButton>(string? data, out TButton button, out ConversationBinding? binding)
        where TButton : struct
    {
        var codec = ButtonCodec.Of(typeof(TButton));
        if (data is not null && codec.TryDecode(data, out var decoded, out binding))
        {
            button = (TButton)decoded!;
            return true;
        }

        button = default;
        binding = null;
        return false;
    }

    /// <summary>A callback button labelled <paramref name="text"/> that carries <paramref name="button"/>.</summary>
    /// <exception cref="ArgumentException">As <see cref="Encode{TButton}(TButton)"/>.</exception>
    /// <exception cref="InvalidOperationException">As <see cref="Encode{TButton}(TButton)"/>.</exception>
    public static InlineKeyboardButton Button<TButton>(string text, TButton button)
        where TButton : struct => InlineKeyboardButton.WithCallbackData(text, Encode(button));

    /// <summary>
    /// A callback button labelled <paramref name="text"/> that carries <paramref name="button"/> bound to a
    /// conversation run.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// As <see cref="Encode{TButton}(TButton, ConversationBinding)"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">As <see cref="Encode{TButton}(TButton)"/>.</exception>
    public static InlineKeyboardButton Button<TButton>(string text, TButton button, ConversationBinding binding)
        where TButton : struct => InlineKeyboardButton.WithCallbackData(text, Encode(button, binding));
}
