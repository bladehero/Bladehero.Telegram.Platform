using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.MyChatMembers;

/// <summary>
/// A command for changes to the bot's own membership, such as being added to a group or blocked in a private chat.
/// </summary>
public abstract class MyChatMemberCommand : TypedTelegramCommand<ChatMemberUpdated>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.MyChatMember;
}
