using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.InlineQueries;

public abstract class InlineQueryCommand : TypedTelegramCommand<InlineQuery>
{
    protected override UpdateType Type => UpdateType.InlineQuery;
}
