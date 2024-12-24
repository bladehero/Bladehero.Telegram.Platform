using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.ChosenInlineResults;

public abstract class ChosenInlineResultCommand : TypedTelegramCommand<ChosenInlineResult>
{
    protected override UpdateType Type => UpdateType.ChosenInlineResult;
}
