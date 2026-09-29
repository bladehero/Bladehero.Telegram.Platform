using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.CommandMenu;

/// <summary>
/// The <see cref="BotCommandAttribute"/> commands in menu order, e.g. to render a <c>/help</c> reply.
/// </summary>
public interface IBotCommandMenu
{
    IReadOnlyList<BotCommand> Commands { get; }
}
