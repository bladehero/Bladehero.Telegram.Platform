using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.PollAnswers;

/// <summary>A command for votes changed in the non-anonymous polls the bot sent.</summary>
public abstract class PollAnswerCommand : TypedTelegramCommand<PollAnswer>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.PollAnswer;
}
