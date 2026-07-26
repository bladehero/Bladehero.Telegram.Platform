using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChosenInlineResults;

public abstract class ChosenInlineResultCommand : TypedTelegramCommand<ChosenInlineResult>
{
    protected override UpdateType Type => UpdateType.ChosenInlineResult;
}
