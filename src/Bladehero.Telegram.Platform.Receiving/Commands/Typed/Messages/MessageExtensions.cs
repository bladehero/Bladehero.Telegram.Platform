using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

public static class MessageExtensions
{
    /// <summary>
    /// Whether the message is the given bot command — <c>/last</c>, <c>/last@BotName</c> or <c>/last 10</c>.
    /// </summary>
    public static bool IsCommand(this Message message, string command) =>
        message.Text?.Split(' ', '@')[0].Equals(command, StringComparison.OrdinalIgnoreCase) is true;

    /// <summary>
    /// The text following a bot command, or <c>null</c> when the message carries none.
    /// </summary>
    public static string? ArgumentsOf(this Message message, string command)
    {
        if (!message.IsCommand(command))
        {
            return null;
        }

        var separator = message.Text!.IndexOf(' ');
        return separator < 0 ? null
            : message.Text[(separator + 1)..].Trim() is { Length: > 0 } text ? text
            : null;
    }
}
