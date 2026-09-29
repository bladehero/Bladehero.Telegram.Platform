using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChatJoinRequests;

/// <summary>
/// A command for requests to join a chat, which Telegram sends only to an administrator allowed to invite users.
/// </summary>
public abstract class ChatJoinRequestCommand : TypedTelegramCommand<ChatJoinRequest>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.ChatJoinRequest;
}
