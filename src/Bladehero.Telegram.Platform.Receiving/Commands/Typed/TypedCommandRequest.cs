using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed;

public sealed class TypedCommandRequest<TPayload>
    where TPayload : class
{
    internal TypedCommandRequest(int updateId, TPayload payload, ITelegramBotClient client)
    {
        Payload = payload;
        Client = client;
        UpdateId = updateId;
    }

    public int UpdateId { get; set; }

    public TPayload Payload { get; }

    public ITelegramBotClient Client { get; }

    public void Deconstruct(out int updateId, out TPayload payload, out ITelegramBotClient client)
    {
        updateId = UpdateId;
        payload = Payload;
        client = Client;
    }
}
