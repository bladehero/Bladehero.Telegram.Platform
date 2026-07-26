using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Errors;

public sealed class TelegramError
{
    internal TelegramError(Exception exception, ITelegramBotClient client)
    {
        Exception = exception;
        Client = client;
    }

    public Exception Exception { get; }

    public ITelegramBotClient Client { get; }
}
