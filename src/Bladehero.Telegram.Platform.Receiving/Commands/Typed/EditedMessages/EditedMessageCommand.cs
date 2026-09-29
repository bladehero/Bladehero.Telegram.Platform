using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedMessages;

/// <summary>A command for edits to messages, each edited message a <see cref="Message"/>.</summary>
public abstract class EditedMessageCommand : TypedTelegramCommand<Message>
{
    /// <inheritdoc/>
    protected override UpdateType Type => UpdateType.EditedMessage;
}
