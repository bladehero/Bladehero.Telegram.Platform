using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution;

public sealed class CommandRequest
{
    internal CommandRequest(Update update, ITelegramBotClient client)
    {
        Update = update;
        Client = client;
    }

    public Update Update { get; }

    public ITelegramBotClient Client { get; }

    public void Deconstruct(out Update update, out ITelegramBotClient client)
    {
        update = Update;
        client = Client;
    }
}
