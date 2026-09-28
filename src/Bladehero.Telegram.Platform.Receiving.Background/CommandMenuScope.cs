namespace Bladehero.Telegram.Platform.Receiving.Background;

/// <summary>
/// Which chats the command menu is published to. Telegram keeps a separate menu per scope and shows each user the
/// most specific one that applies to them.
/// </summary>
public enum CommandMenuScope
{
    /// <summary>Every chat without a more specific menu of its own.</summary>
    Default,

    AllPrivateChats,

    AllGroupChats,

    AllChatAdministrators,
}
