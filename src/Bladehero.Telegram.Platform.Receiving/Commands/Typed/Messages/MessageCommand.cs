using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

/// <summary>
/// A command for new messages of any kind, in private chats and groups; channel posts come to
/// <see cref="ChannelPosts.ChannelPostCommand"/>.
/// </summary>
public abstract class MessageCommand : TypedTelegramCommand<Message>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.Message;
}
