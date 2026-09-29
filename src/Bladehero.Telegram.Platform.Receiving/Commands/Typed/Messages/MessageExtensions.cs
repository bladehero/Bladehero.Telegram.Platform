using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>Reads bot commands such as <c>/last 10</c> from messages.</summary>
public static class MessageExtensions
{
    /// <summary>
    /// Whether the message is the bot command: <c>/last</c>, <c>/last@BotName</c> or <c>/last 10</c>. The command ends
    /// at the first whitespace, a newline or tab included.
    /// </summary>
    public static bool IsCommand(this Message message, string command) =>
        message.Text is { } text && CommandOf(text).Equals(command, StringComparison.OrdinalIgnoreCase);

    /// <summary>The text after the bot command and its whitespace, trimmed, or <c>null</c> when there is none.</summary>
    public static string? ArgumentsOf(this Message message, string command)
    {
        if (!message.IsCommand(command))
        {
            return null;
        }

        var text = message.Text!;
        return text[EndOfCommand(text)..].Trim() is { Length: > 0 } arguments ? arguments : null;
    }

    // The text up to the first whitespace, without an @username.
    private static string CommandOf(string text)
    {
        var token = text[..EndOfCommand(text)];
        var at = token.IndexOf('@');
        return at < 0 ? token : token[..at];
    }

    private static int EndOfCommand(string text)
    {
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return end;
    }
}
