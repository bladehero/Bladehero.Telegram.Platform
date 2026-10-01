using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.PreCheckoutQueries;

/// <summary>
/// A command for pre-checkout queries, the last step before a payment; Telegram waits 10 seconds for the answer.
/// </summary>
public abstract class PreCheckoutQueryCommand : TypedTelegramCommand<PreCheckoutQuery>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.PreCheckoutQuery;
}
