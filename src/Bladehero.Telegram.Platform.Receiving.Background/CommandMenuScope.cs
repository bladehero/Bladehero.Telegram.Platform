namespace Bladehero.Telegram.Platform.Receiving.Background;

/// <summary>The chats the command menu is published to; Telegram keeps one menu per scope.</summary>
public enum CommandMenuScope
{
    /// <summary>Every chat without a more specific menu of its own.</summary>
    Default,

    AllPrivateChats,

    AllGroupChats,

    AllChatAdministrators,
}
