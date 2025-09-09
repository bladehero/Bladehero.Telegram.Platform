using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Telegram.DotNet.Platform.Receiving.Background;

public sealed class TelegramReceiverConfiguration : TelegramBotConfiguration
{
    public int? Offset { get; set; }
    public UpdateType[]? AllowedUpdates { get; set; }
    public int? Limit { get; set; }
    public bool DropPendingUpdates { get; set; }

    internal ReceiverOptions ToOptions() =>
        new()
        {
            Offset = Offset,
            AllowedUpdates = AllowedUpdates,
            Limit = Limit,
            DropPendingUpdates = DropPendingUpdates,
        };
}
