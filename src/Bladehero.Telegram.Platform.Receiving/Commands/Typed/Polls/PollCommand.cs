using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Polls;

/// <summary>
/// A command for new poll states, which Telegram sends only for polls the bot sent and polls stopped manually.
/// </summary>
public abstract class PollCommand : TypedTelegramCommand<Poll>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.Poll;
}
