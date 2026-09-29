using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChatMembers;

/// <summary>
/// A command for changes to chat members, which Telegram sends only to an administrator that asks for
/// <c>ChatMember</c> in its allowed updates.
/// </summary>
public abstract class ChatMemberCommand : TypedTelegramCommand<ChatMemberUpdated>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.ChatMember;
}
