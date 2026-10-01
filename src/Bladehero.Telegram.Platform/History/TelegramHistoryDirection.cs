namespace Bladehero.Telegram.Platform.History;

/// <summary>Which way an entry went.</summary>
public enum TelegramHistoryDirection
{
    /// <summary>An update Telegram sent the bot.</summary>
    Incoming,

    /// <summary>A Bot API call the bot made.</summary>
    Outgoing,
}
