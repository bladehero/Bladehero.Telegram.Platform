using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Replies: a message carries the one it replies to, without that one's own reply.
public sealed partial class FakeBotApi
{
    // The message as a reply shows it, or null when it's no longer in the chat.
    internal JsonObject? ReplyTarget(long chatId, int messageId)
    {
        lock (_gate)
        {
            return _chats.TryGetValue(chatId, out var chat) ? AsReplyTarget(chat.Find(messageId)) : null;
        }
    }

    private static JsonObject? AsReplyTarget(JsonObject? message)
    {
        if (message?.DeepClone().AsObject() is not { } target)
        {
            return null;
        }

        target.Remove("reply_to_message");
        return target;
    }
}
