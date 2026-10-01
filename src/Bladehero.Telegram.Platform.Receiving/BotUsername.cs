namespace Bladehero.Telegram.Platform.Receiving;

// The username of the bot handling the current update, for IsCommand; null outside an update or while unknown.
internal static class BotUsername
{
    internal static readonly AsyncLocal<string?> Current = new();
}
