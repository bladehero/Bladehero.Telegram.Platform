namespace Bladehero.Telegram.Platform.Hub;

/// <summary>
/// Passes that act for a person, for the one kind of app the hub lets do that: a delegate such as Home, which connects a
/// person's Claude to the apps they use. Any other app is refused by the hub.
/// </summary>
/// <remarks>
/// Registered by <c>AddTelegramHubPasses</c>, beside <see cref="ITelegramHubPasses"/>. Asked of the hub through the bot's
/// own client, under the delegate's hub key.
/// </remarks>
public interface ITelegramHubPersonPasses
{
    /// <summary>
    /// A pass for <paramref name="audience"/> that acts for <paramref name="person"/> through
    /// <paramref name="connection"/>, from the cache while it has more than five minutes left, else a new one. Passes are
    /// kept per person, connection, audience and scopes asked, so one person's is never handed out for another.
    /// </summary>
    /// <param name="audience">The callee's slug, such as <c>gym</c>.</param>
    /// <param name="person">The person's Telegram id.</param>
    /// <param name="connection">The person's connection the call comes through.</param>
    /// <param name="scopes">
    /// The connection's scopes there; <c>null</c> for everything the hub allows. The hub never gives more than the owner's
    /// cap and the scopes the callee lets people grant.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait and the request.</param>
    /// <exception cref="ArgumentException">The connection's id is empty or its name isn't one the hub takes.</exception>
    /// <exception cref="TelegramHubPassUnavailableException">
    /// The hub gave none (it's down; the person may not use that app; nothing is granted) and no cached pass is still good.
    /// </exception>
    ValueTask<TelegramHubPass> GetAsync(
        string audience,
        long person,
        TelegramHubPersonConnection connection,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The apps <paramref name="person"/> may use that take person passes, each with what the person may grant there.
    /// Not cached: the caller keeps its own copy as long as it likes.
    /// </summary>
    /// <param name="person">The person's Telegram id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="TelegramHubPassUnavailableException">The hub refused, or couldn't be reached.</exception>
    ValueTask<IReadOnlyList<TelegramHubApp>> GetAppsAsync(long person, CancellationToken cancellationToken = default);

    /// <summary>Drops every pass kept for this connection, as when it is deleted, so none of them is handed out again.</summary>
    /// <param name="connectionId">The connection's id.</param>
    void Forget(Guid connectionId);
}
