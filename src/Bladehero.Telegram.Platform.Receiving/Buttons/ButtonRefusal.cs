using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>A tap on a typed button that no command took.</summary>
/// <param name="query">The tap.</param>
/// <param name="client">The bot's client.</param>
/// <param name="reason">Why no command took it.</param>
/// <param name="buttonType">The button type the data's prefix names.</param>
public sealed class ButtonRefusal(
    CallbackQuery query,
    ITelegramBotClient client,
    ButtonRefusalReason reason,
    Type buttonType
)
{
    /// <summary>The tap.</summary>
    public CallbackQuery Query { get; } = query;

    /// <summary>The bot's client, to answer the tap.</summary>
    public ITelegramBotClient Client { get; } = client;

    /// <summary>Why no command took the tap.</summary>
    public ButtonRefusalReason Reason { get; } = reason;

    /// <summary>The button type the data's prefix names.</summary>
    public Type ButtonType { get; } = buttonType;
}
