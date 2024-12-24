using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.ShippingQueries;

public abstract class ShippingQueryCommand : TypedTelegramCommand<ShippingQuery>
{
    protected override UpdateType Type => UpdateType.ShippingQuery;
}
