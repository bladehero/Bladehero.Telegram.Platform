using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>One user in one chat, so each group member has a conversation of their own.</summary>
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
