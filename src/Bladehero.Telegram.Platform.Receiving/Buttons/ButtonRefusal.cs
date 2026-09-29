using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>
/// A tap on a <see cref="ButtonAttribute"/> button that no command took, as an <see cref="IButtonRefusalHandler"/> gets
/// it.
/// </summary>
/// <param name="query">The tap.</param>
/// <param name="client">The bot's client, to answer the tap.</param>
/// <param name="reason">Why no command took it.</param>
/// <param name="buttonType">The button type whose prefix the data carries.</param>
public sealed class ButtonRefusal(
    CallbackQuery query,
    ITelegramBotClient client,
    ButtonRefusalReason reason,
    Type buttonType
)
{
    /// <summary>The tap, with the data and the message the button is on.</summary>
    public CallbackQuery Query { get; } = query;

    /// <summary>The bot's client, to answer the tap and, if need be, edit the message.</summary>
    public ITelegramBotClient Client { get; } = client;

    /// <summary>Why no command took the tap.</summary>
    public ButtonRefusalReason Reason { get; } = reason;

    /// <summary>The <see cref="ButtonAttribute"/> type whose prefix the data carries.</summary>
    public Type ButtonType { get; } = buttonType;
}
