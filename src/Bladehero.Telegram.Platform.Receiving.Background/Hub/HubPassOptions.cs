using Microsoft.AspNetCore.Authentication;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>How this app checks the hub's passes, for <c>AddHubPasses</c>. The defaults suit an app behind the hub.</summary>
public sealed class HubPassOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// This app's slug, which a pass must name as its <c>aud</c>. By default the slug in the bot's token, a hub key; set it
    /// where the token isn't one, as in a test against the fake Telegram.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>The hub a pass must come from, as its <c>iss</c>. By default the issuer the hub's key set names.</summary>
    public string? Issuer { get; set; }

    /// <summary>Where the hub's public keys are. By default <c>/hub/keys</c> at the bot's <c>ApiBaseUrl</c>.</summary>
    public Uri? KeysUrl { get; set; }

    /// <summary>
    /// The app's public host names. A request for one came through the edge, and no pass counts on it. The host of
    /// <see cref="TelegramHubConfiguration.Site"/> is one already.
    /// </summary>
    public IList<string> PublicHosts { get; set; } = [];

    /// <summary>
    /// A caller's id here in place of <see cref="HubPassDefaults.ClientIdOf"/>, by its slug: for an app that wrote under a
    /// connection before, so what it wrote stays its own.
    /// </summary>
    public IDictionary<string, Guid> ClientIds { get; set; } = new Dictionary<string, Guid>(StringComparer.Ordinal);

    /// <summary>How far the clocks may differ on <c>nbf</c> and <c>exp</c>. 30 seconds by default.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the keys are read again. An hour by default.</summary>
    public TimeSpan KeysRefreshEvery { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How soon the keys are read again after a failure, or for a key id nobody has seen yet. 30 seconds by default.
    /// </summary>
    public TimeSpan KeysRetryEvery { get; set; } = TimeSpan.FromSeconds(30);
}
