using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed.Messages;

public abstract class MessageCommand : TypedTelegramCommand<Message>
{
    protected override UpdateType Type => UpdateType.Message;
}
