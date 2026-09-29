using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution;

/// <summary>The update being handled and the bot's client, as an <see cref="ITelegramCommand"/> gets them.</summary>
public sealed class CommandRequest
{
    internal CommandRequest(Update update, ITelegramBotClient client)
    {
        Update = update;
        Client = client;
    }

    /// <summary>The whole update, of any type.</summary>
    public Update Update { get; }

    /// <summary>The bot's client, for replies and any other Bot API call.</summary>
    public ITelegramBotClient Client { get; }

    /// <summary>
    /// Deconstructs into <see cref="Update"/> and <see cref="Client"/>, as in <c>var (update, client) = request;</c>.
    /// </summary>
    public void Deconstruct(out Update update, out ITelegramBotClient client)
    {
        update = Update;
        client = Client;
    }
}
