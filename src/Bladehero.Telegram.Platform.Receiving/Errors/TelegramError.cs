using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Errors;

public sealed class TelegramError
{
    internal TelegramError(Exception exception, ITelegramBotClient client, Update? update = null)
    {
        Exception = exception;
        Client = client;
        Update = update;
    }

    public Exception Exception { get; }

    public ITelegramBotClient Client { get; }

    /// <summary>The update being handled when the error happened; <c>null</c> for a polling error.</summary>
    public Update? Update { get; }
}
