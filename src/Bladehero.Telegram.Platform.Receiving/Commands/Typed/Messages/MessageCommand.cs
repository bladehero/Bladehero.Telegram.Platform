using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

public abstract class MessageCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.Message;
}
