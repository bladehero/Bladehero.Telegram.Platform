using Microsoft.Extensions.Options;
using Telegram.Bot.Polling;

namespace Telegram.DotNet.Platform.Receiving.Background;

internal class ReceiverOptionsProvider(
    IReceiverOptionsMapper mapper,
    IOptionsMonitor<ReceiverConfiguration> optionsMonitor
) : IReceiverOptionsProvider
{
    public ReceiverOptions Get() => mapper.ToOptions(optionsMonitor.CurrentValue);
}
