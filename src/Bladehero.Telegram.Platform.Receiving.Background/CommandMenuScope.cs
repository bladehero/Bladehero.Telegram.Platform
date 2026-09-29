namespace Bladehero.Telegram.Platform.Receiving.Background;

/// <summary>The chats the command menu is published to; Telegram keeps one menu per scope.</summary>
public enum CommandMenuScope
{
    /// <summary>Every chat without a more specific menu of its own.</summary>
    Default,

    /// <summary>
    /// Every private chat, as Telegram's <see cref="global::Telegram.Bot.Types.BotCommandScopeAllPrivateChats"/>.
    /// </summary>
    AllPrivateChats,

    /// <summary>
    /// Every group and supergroup, as Telegram's <see cref="global::Telegram.Bot.Types.BotCommandScopeAllGroupChats"/>.
    /// </summary>
    AllGroupChats,

    /// <summary>
    /// The administrators of every group and supergroup, as Telegram's
    /// <see cref="global::Telegram.Bot.Types.BotCommandScopeAllChatAdministrators"/>.
    /// </summary>
    AllChatAdministrators,
}
