using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

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

    /// <summary>
    /// Where the hub's public keys are. By default <c>/hub/keys</c> at the bot's <c>ApiBaseUrl</c>; with that blank or
    /// Telegram's the bot isn't behind a hub, so no keys are read and no pass is taken.
    /// </summary>
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

    /// <summary>
    /// Turns a person pass into the principal this app's own key would give that person, or <c>null</c> to refuse it (a
    /// person the app doesn't know and won't admit). Without it, no person pass is taken, as before 10.8.
    /// </summary>
    /// <remarks>
    /// Called only for a pass that has passed every check: from a caller in <see cref="Delegates"/>, on a path in
    /// <see cref="PeoplePaths"/>, its scopes already cut to the ones this app marks <see cref="TelegramHubScope.ForPeople"/>,
    /// at least one left. The principal then gets <see cref="HubPassDefaults.HubPersonClaim"/> and
    /// <see cref="HubPassDefaults.HubViaClaim"/>; any <c>hub_*</c> claim it had, and any <c>scope</c> claim outside
    /// <see cref="HubPassPerson.Scopes"/>, are taken off.
    /// </remarks>
    public Func<HubPassPerson, CancellationToken, Task<ClaimsPrincipal?>>? People { get; set; }

    /// <summary>
    /// The callers whose person passes count here: <c>home</c> by default. The hub gives person passes to its own
    /// delegates only; this says it again here, so a mistake there doesn't open this app. A delegate's pass that names no
    /// person is refused: it never acts as itself.
    /// </summary>
    public IList<string> Delegates { get; set; } = ["home"];

    /// <summary>
    /// The paths a person pass counts on, each with whatever is below it: <c>/mcp</c> by default. Anywhere else, such as
    /// <c>/internal/*</c>, <c>/api/*</c> or a route with <c>RequireHubScope</c>, it gets 401.
    /// </summary>
    public IList<PathString> PeoplePaths { get; set; } = [new PathString("/mcp")];

    /// <summary>How far the clocks may differ on <c>nbf</c> and <c>exp</c>. 30 seconds by default.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the keys are read again. An hour by default.</summary>
    public TimeSpan KeysRefreshEvery { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How soon the keys are read again after a failure, or for a key id nobody has seen yet. 30 seconds by default.
    /// </summary>
    public TimeSpan KeysRetryEvery { get; set; } = TimeSpan.FromSeconds(30);
}
