using Riok.Mapperly.Abstractions;
using Telegram.Bot.Polling;

namespace Telegram.DotNet.Platform.Receiving.Background;

internal interface IReceiverOptionsMapper
{
    ReceiverOptions ToOptions(ReceiverConfiguration configuration);
}

[Mapper]
internal partial class ReceiverOptionsMapper : IReceiverOptionsMapper
{
    public partial ReceiverOptions ToOptions(ReceiverConfiguration configuration);
}
