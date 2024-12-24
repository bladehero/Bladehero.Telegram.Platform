using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.EditedMessages;

public abstract class EditedMessageCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.EditedMessage;
}
