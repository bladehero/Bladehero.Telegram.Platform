using Telegram.Bot;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed;

public sealed class TypedCommandRequest<TPayload>
    where TPayload : class
{
    internal TypedCommandRequest(TPayload payload, ITelegramBotClient client)
    {
        Payload = payload;
        Client = client;
    }

    public int UpdateId { get; set; }

    public TPayload Payload { get; }

    public ITelegramBotClient Client { get; }
}
