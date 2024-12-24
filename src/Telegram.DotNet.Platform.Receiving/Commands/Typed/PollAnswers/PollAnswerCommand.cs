using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.PollAnswers;

public abstract class PollAnswerCommand : TypedTelegramCommand<PollAnswer>
{
    protected override UpdateType Type => UpdateType.PollAnswer;
}
