using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedMessages;

public abstract class EditedMessageCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.EditedMessage;
}
