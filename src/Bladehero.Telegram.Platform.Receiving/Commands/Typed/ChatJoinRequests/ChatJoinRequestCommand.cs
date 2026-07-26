using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChatJoinRequests;

public abstract class ChatJoinRequestCommand : TypedTelegramCommand<ChatJoinRequest>
{
    protected override UpdateType Type => UpdateType.ChatJoinRequest;
}
