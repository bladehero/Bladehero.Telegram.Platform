namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Passes from the hub, to call another app inside the same network without a key made by hand.</summary>
/// <remarks>
/// Registered by <c>AddTelegramHubPasses</c> and by <c>AddHubPass</c> on a named <see cref="HttpClient"/>, which attaches
/// them itself. A pass is asked of the hub through the bot's own client, under the app's hub key.
/// </remarks>
public interface ITelegramHubPasses
{
    /// <summary>
    /// A pass for <paramref name="audience"/>, from the cache while it has more than five minutes left, else a new one.
    /// One request per audience goes to the hub at a time.
    /// </summary>
    /// <param name="audience">The callee's slug, such as <c>familybudget</c>.</param>
    /// <param name="scopes">
    /// Fewer scopes than were granted, if the call needs only those; <c>null</c> for all of them. The hub never gives more.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait and the request.</param>
    /// <exception cref="TelegramHubPassUnavailableException">
    /// The hub gave none (it's down, or the caller has no grant there) and no cached pass is still good.
    /// </exception>
    ValueTask<TelegramHubPass> GetAsync(
        string audience,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default
    );
}
