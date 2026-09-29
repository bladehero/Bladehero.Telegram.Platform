using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>
/// A command for button taps (callback queries); <see cref="CallbackQueryCommand{TData}"/> also parses the data.
/// </summary>
public abstract class CallbackQueryCommand : TypedTelegramCommand<CallbackQuery>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.CallbackQuery;
}
