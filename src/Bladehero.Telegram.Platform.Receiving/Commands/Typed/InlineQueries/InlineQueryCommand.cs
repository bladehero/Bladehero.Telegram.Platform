using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.InlineQueries;

/// <summary>
/// A command for inline queries, which users type as <c>@YourBot query</c> in any chat once inline mode is on in
/// @BotFather.
/// </summary>
public abstract class InlineQueryCommand : TypedTelegramCommand<InlineQuery>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.InlineQuery;
}
