using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Errors;

/// <summary>An error the receiver hit, as <see cref="ITelegramErrorHandler"/> gets it.</summary>
public sealed class TelegramError
{
    internal TelegramError(Exception exception, ITelegramBotClient client, Update? update = null)
    {
        Exception = exception;
        Client = client;
        Update = update;
    }

    /// <summary>What was thrown.</summary>
    public Exception Exception { get; }

    /// <summary>The bot's client, e.g. to apologize in the chat of <see cref="Update"/>.</summary>
    public ITelegramBotClient Client { get; }

    /// <summary>The update being handled when the error happened; <c>null</c> for a polling error.</summary>
    public Update? Update { get; }
}
