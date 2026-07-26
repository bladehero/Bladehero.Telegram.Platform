using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedChannelPosts;

public abstract class EditedChannelPostCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.EditedChannelPost;
}
