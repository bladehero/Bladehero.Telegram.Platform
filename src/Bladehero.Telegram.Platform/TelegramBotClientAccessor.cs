using Telegram.Bot;

namespace Bladehero.Telegram.Platform;

internal sealed class TelegramBotClientAccessor(ITelegramBotClient client)
{
    public ITelegramBotClient Client { get; } = client;
}
