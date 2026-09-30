using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>A tap on a typed button that no command took.</summary>
/// <param name="query">The tap.</param>
/// <param name="client">The bot's client.</param>
/// <param name="reason">Why no command took it.</param>
/// <param name="buttonType">The button type the data's prefix names.</param>
/// <param name="binding">The tapped button's binding, if it is bound.</param>
/// <param name="conversation">The tapper's conversation when it was read, else <c>null</c>.</param>
public sealed class ButtonRefusal(
    CallbackQuery query,
    ITelegramBotClient client,
    ButtonRefusalReason reason,
    Type buttonType,
    ConversationBinding? binding = null,
    ConversationState? conversation = null
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

    /// <summary>The tapped button's binding, if it is bound.</summary>
    public ConversationBinding? Binding { get; } = binding;

    /// <summary>The tapper's conversation when it was read, else <c>null</c>.</summary>
    public ConversationState? Conversation { get; } = conversation;

    /// <summary>Answers the tap; <c>false</c> when Telegram says it came too late.</summary>
    /// <param name="text">The notification or alert; <c>null</c> answers silently.</param>
    /// <param name="showAlert">Whether to show <paramref name="text"/> as an alert.</param>
    /// <param name="token">Cancels the call.</param>
    public async Task<bool> AnswerAsync(string? text = null, bool showAlert = false, CancellationToken token = default)
    {
        try
        {
            await Client.AnswerCallbackQuery(Query.Id, text, showAlert, cancellationToken: token);
            return true;
        }
        catch (ApiRequestException error) when (error.Message.Contains("query is too old"))
        {
            return false;
        }
    }
}
