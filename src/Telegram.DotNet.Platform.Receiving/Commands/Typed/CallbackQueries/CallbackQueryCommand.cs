using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.CallbackQueries;

public abstract class CallbackQueryCommand : TypedTelegramCommand<CallbackQuery>
{
    protected override UpdateType Type => UpdateType.CallbackQuery;
}
