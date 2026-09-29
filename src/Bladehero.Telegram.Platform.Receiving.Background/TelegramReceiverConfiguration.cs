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
    /// Sends the <c>[BotCommand]</c> menu to Telegram on startup. Turn off where another environment shares the token.
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
