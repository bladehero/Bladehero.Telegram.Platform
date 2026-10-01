using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Background;

/// <summary>
/// The settings of a long-polling bot; the webhook's <see cref="Webhook.TelegramWebhookConfiguration"/> extends them.
/// </summary>
public class TelegramReceiverConfiguration : TelegramBotConfiguration
{
    /// <summary>The update id to resume polling from; long polling only.</summary>
    public int? Offset { get; set; }

    /// <summary>
    /// The update types to receive. Unset asks for Telegram's default set (all but <c>ChatMember</c> and reactions)
    /// explicitly, so a list an earlier deployment set doesn't linger.
    /// </summary>
    public UpdateType[]? AllowedUpdates { get; set; }

    /// <summary>
    /// The most updates one poll fetches: 1-100, or Telegram's default of 100 when unset; long polling only.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>Discards the updates queued while the bot was down instead of handling them on startup.</summary>
    public bool DropPendingUpdates { get; set; }

    /// <summary>
    /// Sends the <c>[BotCommand]</c> menu to Telegram on startup. Turn off where another environment shares the token.
    /// </summary>
    public bool SyncCommandMenu { get; set; } = true;

    /// <summary>
    /// The chats the command menu is published to; <see cref="CommandMenuScope.Default"/> unless set.
    /// </summary>
    public CommandMenuScope CommandMenuScope { get; set; }

    // Unset asks for Telegram's default explicitly: an omitted list would keep one an earlier deployment set.
    internal ReceiverOptions ToOptions() =>
        new()
        {
            Offset = Offset,
            AllowedUpdates = AllowedUpdates ?? [],
            Limit = Limit,
            DropPendingUpdates = DropPendingUpdates,
        };
}
