using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChatMembers;

public abstract class ChatMemberCommand : TypedTelegramCommand<ChatMemberUpdated>
{
    protected override UpdateType Type => UpdateType.ChatMember;
}
