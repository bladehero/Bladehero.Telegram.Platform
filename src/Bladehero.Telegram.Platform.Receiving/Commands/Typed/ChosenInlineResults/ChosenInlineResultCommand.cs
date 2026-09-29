using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChosenInlineResults;

/// <summary>
/// A command for the inline results users pick, which Telegram sends only with inline feedback turned on in
/// @BotFather.
/// </summary>
public abstract class ChosenInlineResultCommand : TypedTelegramCommand<ChosenInlineResult>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.ChosenInlineResult;
}
