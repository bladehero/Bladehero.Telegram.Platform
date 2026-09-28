using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Background;

public class TelegramReceiverConfiguration : TelegramBotConfiguration
{
    public int? Offset { get; set; }
    public UpdateType[]? AllowedUpdates { get; set; }
    public int? Limit { get; set; }
    public bool DropPendingUpdates { get; set; }

    /// <summary>
    /// Whether the menu declared with <c>[BotCommand]</c> is sent to Telegram on startup. Turn it off where the host
    /// shares a bot token with another environment, so it does not overwrite that environment's menu.
    /// </summary>
    public bool SyncCommandMenu { get; set; } = true;

    public CommandMenuScope CommandMenuScope { get; set; }

    internal ReceiverOptions ToOptions() =>
        new()
        {
            Offset = Offset,
            AllowedUpdates = AllowedUpdates,
            Limit = Limit,
            DropPendingUpdates = DropPendingUpdates,
        };
}
