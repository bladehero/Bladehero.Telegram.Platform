using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.InlineQueries;

public abstract class InlineQueryCommand : TypedTelegramCommand<InlineQuery>
{
    protected override UpdateType Type => UpdateType.InlineQuery;
}
