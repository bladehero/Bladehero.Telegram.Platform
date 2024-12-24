using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.Polls;

public abstract class PollCommand : TypedTelegramCommand<Poll>
{
    protected override UpdateType Type => UpdateType.Poll;
}
