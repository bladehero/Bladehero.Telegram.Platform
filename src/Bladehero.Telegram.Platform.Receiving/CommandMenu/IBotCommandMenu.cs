using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.CommandMenu;

/// <summary>
/// The <see cref="BotCommandAttribute"/> commands in menu order, e.g. to render a <c>/help</c> reply.
/// </summary>
public interface IBotCommandMenu
{
    /// <summary>The menu's entries in order; empty when no command has a <see cref="BotCommandAttribute"/>.</summary>
    IReadOnlyList<BotCommand> Commands { get; }
}
