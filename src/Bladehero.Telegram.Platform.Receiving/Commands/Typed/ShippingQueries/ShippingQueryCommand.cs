using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ShippingQueries;

public abstract class ShippingQueryCommand : TypedTelegramCommand<ShippingQuery>
{
    protected override UpdateType Type => UpdateType.ShippingQuery;
}
