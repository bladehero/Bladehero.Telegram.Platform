using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Background;

public sealed class ReceiverConfiguration
{
    internal ReceiverConfiguration() { }

    public int? Offset { get; set; }
    public UpdateType[]? AllowedUpdates { get; set; }
    public int? Limit { get; set; }
    public bool ThrowPendingUpdates { get; set; }
}
