using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedChannelPosts;

/// <summary>A command for edits to channel posts, each edited post a <see cref="Message"/>.</summary>
public abstract class EditedChannelPostCommand : TypedTelegramCommand<Message>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.EditedChannelPost;
}
