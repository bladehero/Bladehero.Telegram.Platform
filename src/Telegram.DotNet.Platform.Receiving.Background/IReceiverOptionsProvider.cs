using Telegram.Bot.Polling;

namespace Telegram.DotNet.Platform.Receiving.Background;

internal interface IReceiverOptionsProvider
{
    ReceiverOptions Get();
}
