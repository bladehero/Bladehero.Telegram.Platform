using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.ChatJoinRequests;

public abstract class ChatJoinRequestCommand : TypedTelegramCommand<ChatJoinRequest>
{
    protected override UpdateType Type => UpdateType.ChatJoinRequest;
}
