using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.PreCheckoutQueries;

public abstract class PreCheckoutQueryCommand : TypedTelegramCommand<PreCheckoutQuery>
{
    protected override UpdateType Type => UpdateType.PreCheckoutQuery;
}
