using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.PollAnswers;

public abstract class PollAnswerCommand : TypedTelegramCommand<PollAnswer>
{
    protected override UpdateType Type => UpdateType.PollAnswer;
}
