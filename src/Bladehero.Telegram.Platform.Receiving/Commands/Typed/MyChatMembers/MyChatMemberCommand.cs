using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.MyChatMembers;

public abstract class MyChatMemberCommand : TypedTelegramCommand<ChatMemberUpdated>
{
    protected override UpdateType Type => UpdateType.MyChatMember;
}
