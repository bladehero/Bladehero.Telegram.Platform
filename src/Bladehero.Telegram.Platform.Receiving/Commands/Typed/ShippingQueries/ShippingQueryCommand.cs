using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ShippingQueries;

/// <summary>A command for shipping queries, sent for invoices with a flexible price.</summary>
public abstract class ShippingQueryCommand : TypedTelegramCommand<ShippingQuery>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.ShippingQuery;
}
