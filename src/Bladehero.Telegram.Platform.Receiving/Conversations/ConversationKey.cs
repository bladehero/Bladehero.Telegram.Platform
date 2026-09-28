using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Whose conversation an update belongs to: one user in one chat.
/// </summary>
/// <remarks>
/// Keyed on the user as well as the chat so that in a group every member runs a conversation of their own, and
/// one member's button press never advances another's flow. In a private chat both ids are the same person.
/// </remarks>
public readonly record struct ConversationKey(long ChatId, long UserId)
{
    internal static ConversationKey? For(Update update) =>
        update switch
        {
            { Message: { Chat: { } chat, From: { } user } } => new ConversationKey(chat.Id, user.Id),
            { EditedMessage: { Chat: { } chat, From: { } user } } => new ConversationKey(chat.Id, user.Id),
            { CallbackQuery: { Message.Chat: { } chat, From: { } user } } => new ConversationKey(chat.Id, user.Id),
            _ => null,
        };
}
