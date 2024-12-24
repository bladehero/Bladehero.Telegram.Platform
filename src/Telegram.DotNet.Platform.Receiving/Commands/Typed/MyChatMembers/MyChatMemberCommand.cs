using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.MyChatMembers;

public abstract class MyChatMemberCommand : TypedTelegramCommand<ChatMemberUpdated>
{
    protected override UpdateType Type => UpdateType.MyChatMember;
}
