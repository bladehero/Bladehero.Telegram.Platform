using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>Reads bot commands such as <c>/last 10</c> from messages.</summary>
public static class MessageExtensions
{
    /// <summary>
    /// Whether the message is the bot command, e.g. <c>/last 10</c>; inside an update, not when addressed to another
    /// bot.
    /// </summary>
    /// <remarks>
    /// The command ends where Telegram ends it, so <c>/last.</c> and <c>/last 10</c> are both <c>/last</c>. Outside
    /// an update, or before the bot has learned its username, any <c>@name</c> is accepted.
    /// </remarks>
    public static bool IsCommand(this Message message, string command) =>
        message.IsCommand(command, BotUsername.Current.Value);

    /// <summary>
    /// As <see cref="IsCommand(Message, string)"/>, for the bot named <paramref name="botUsername"/>; <c>null</c>
    /// accepts any.
    /// </summary>
    public static bool IsCommand(this Message message, string command, string? botUsername) =>
        TokenOf(message) is { } token && IsFor(token, command, botUsername);

    /// <summary>The text after the bot command, trimmed, or <c>null</c> when there is none.</summary>
    /// <remarks><c>null</c> too when the message isn't the command, as <see cref="IsCommand(Message, string)"/> decides.</remarks>
    public static string? ArgumentsOf(this Message message, string command) =>
        message.ArgumentsOf(command, BotUsername.Current.Value);

    /// <summary>
    /// As <see cref="ArgumentsOf(Message, string)"/>, for the bot named <paramref name="botUsername"/>; <c>null</c>
    /// accepts any.
    /// </summary>
    public static string? ArgumentsOf(this Message message, string command, string? botUsername)
    {
        if (TokenOf(message) is not { } token || !IsFor(token, command, botUsername))
        {
            return null;
        }

        return message.Text![token.Length..].Trim() is { Length: > 0 } arguments ? arguments : null;
    }

    // The leading command: its bot_command entity, or Telegram's rule when the message has none.
    private static string? TokenOf(Message message)
    {
        if (message.Text is not { } text)
        {
            return null;
        }

        var entity = message.Entities?.FirstOrDefault(x => x is { Type: MessageEntityType.BotCommand, Offset: 0 });
        if (entity is not null && entity.Length <= text.Length)
        {
            return text[..entity.Length];
        }

        return BotCommands.LengthAtStart(text) is { } length ? text[..length] : null;
    }

    private static bool IsFor(string token, string command, string? botUsername)
    {
        var at = token.IndexOf('@');
        var name = at < 0 ? token : token[..at];

        return name.Equals(command, StringComparison.OrdinalIgnoreCase)
            && (
                at < 0
                || botUsername is null
                || token[(at + 1)..].Equals(botUsername, StringComparison.OrdinalIgnoreCase)
            );
    }
}
