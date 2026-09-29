using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed;

/// <summary>
/// The update being handled, as a typed command gets it: its id, its typed payload and the bot's client.
/// </summary>
public sealed class TypedCommandRequest<TPayload>
    where TPayload : class
{
    internal TypedCommandRequest(int updateId, TPayload payload, ITelegramBotClient client)
    {
        Payload = payload;
        Client = client;
        UpdateId = updateId;
    }

    /// <summary>The id of the update that carried <see cref="Payload"/>.</summary>
    public int UpdateId { get; set; }

    /// <summary>The update's content, such as the <c>Message</c> of a message update.</summary>
    public TPayload Payload { get; }

    /// <summary>The bot's client, for replies and any other Bot API call.</summary>
    public ITelegramBotClient Client { get; }

    /// <summary>
    /// Deconstructs into <see cref="UpdateId"/>, <see cref="Payload"/> and <see cref="Client"/>, as in
    /// <c>var (_, message, client) = request;</c>.
    /// </summary>
    public void Deconstruct(out int updateId, out TPayload payload, out ITelegramBotClient client)
    {
        updateId = UpdateId;
        payload = Payload;
        client = Client;
    }
}
