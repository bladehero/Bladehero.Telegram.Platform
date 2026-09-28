using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.CommandMenu;

/// <summary>
/// The bot's command menu — every command marked with <see cref="BotCommandAttribute"/>, in the order Telegram
/// shows them. Inject it to render the same list in a <c>/help</c> reply.
/// </summary>
public interface IBotCommandMenu
{
    IReadOnlyList<BotCommand> Commands { get; }
}
