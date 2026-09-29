using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChannelPosts;

/// <summary>A command for new posts in the channels the bot is in, each post a <see cref="Message"/>.</summary>
public abstract class ChannelPostCommand : TypedTelegramCommand<Message>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.ChannelPost;
}
