using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.ChannelPosts;

public abstract class ChannelPostCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.ChannelPost;
}
