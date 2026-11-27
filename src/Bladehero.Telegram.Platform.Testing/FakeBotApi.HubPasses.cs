using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Hub;

namespace Bladehero.Telegram.Platform.Testing;

// A hub's passes: getHubPass for the app under test as a caller, IssueHubPass for it as a callee, and /hub/keys for its
// verifier, which TelegramTestHost points here.
public sealed partial class FakeBotApi
{
    /// <summary>The issuer of every pass this fake signs, as a hub's <c>PublicUrl</c>.</summary>
    public const string HubIssuer = "https://hub.test";

    private readonly Dictionary<string, string[]> _hubGrants = new(StringComparer.Ordinal);
    private readonly List<(string Id, ECDsa Key)> _hubPassKeys =
    [
        ("test-1", ECDsa.Create(ECCurve.NamedCurves.nistP256)),
    ];

    /// <summary>
    /// The slug the fake hub knows the app under test by: the <c>sub</c> of the passes <c>getHubPass</c> gives it.
    /// <c>app</c> by default.
    /// </summary>
    public string HubSlug { get; set; } = "app";

    /// <summary>The clock the fake hub's passes are dated by. The system's by default.</summary>
    public TimeProvider HubTime { get; set; } = TimeProvider.System;

    /// <summary>While set, <c>/hub/keys</c> can't be reached, as when the hub is down.</summary>
    public bool HubKeysUnreachable { get; set; }

    /// <summary>
    /// Grants the app under test <paramref name="scopes"/> at <paramref name="audience"/>, so <c>getHubPass</c> gives it a
    /// pass there. Without a grant it answers 403 <c>not granted</c>, as a hub does.
    /// </summary>
    /// <param name="audience">The callee's slug.</param>
    /// <param name="scopes">The scopes granted, in the callee's words.</param>
    public void GrantHubPass(string audience, params string[] scopes)
    {
        lock (_gate)
        {
            _hubGrants[audience] = scopes;
        }
    }

    /// <summary>
    /// A pass as the hub would give <paramref name="caller"/> for <paramref name="audience"/>, valid for ten minutes from
    /// <see cref="HubTime"/>'s now, for a request to the app under test.
    /// </summary>
    /// <param name="caller">The calling app's slug.</param>
    /// <param name="audience">The app under test's slug, as its <c>HubPassOptions.Audience</c> says.</param>
    /// <param name="scopes">What the pass carries.</param>
    public string IssueHubPass(string caller, string audience, params string[] scopes) =>
        IssueHubPass(caller, audience, scopes, HubTime.GetUtcNow(), TimeSpan.FromMinutes(10));

    /// <summary>A pass as <see cref="IssueHubPass(string, string, string[])"/> gives, issued and lasting as said.</summary>
    /// <param name="caller">The calling app's slug.</param>
    /// <param name="audience">The app under test's slug.</param>
    /// <param name="scopes">What the pass carries.</param>
    /// <param name="issuedAt">When it was issued.</param>
    /// <param name="lifetime">How long it lasts.</param>
    public string IssueHubPass(
        string caller,
        string audience,
        IEnumerable<string> scopes,
        DateTimeOffset issuedAt,
        TimeSpan lifetime
    ) =>
        SignHubPass(
            Header(),
            new JsonObject
            {
                ["iss"] = HubIssuer,
                ["sub"] = caller,
                ["aud"] = audience,
                ["iat"] = issuedAt.ToUnixTimeSeconds(),
                ["nbf"] = issuedAt.ToUnixTimeSeconds(),
                ["exp"] = (issuedAt + lifetime).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["scope"] = string.Join(' ', scopes),
            }
        );

    /// <summary>
    /// A new signing key, as when the hub's keys change: passes are signed with it from now on, and the old one stays
    /// listed.
    /// </summary>
    public void RotateHubPassKey()
    {
        lock (_gate)
        {
            _hubPassKeys.Insert(0, ($"test-{_hubPassKeys.Count + 1}", ECDsa.Create(ECCurve.NamedCurves.nistP256)));
        }
    }

    // Any header and claims, signed with the current key: for checks that a verifier refuses what a hub never writes.
    internal string SignHubPass(JsonObject header, JsonObject claims)
    {
        lock (_gate)
        {
            return HubPassFormat.Sign(header, claims, _hubPassKeys[0].Key);
        }
    }

    internal JsonObject Header()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["alg"] = HubPassFormat.Algorithm,
                ["kid"] = _hubPassKeys[0].Id,
                ["typ"] = HubPassFormat.Type,
            };
        }
    }

    private JsonObject GetHubPass(JsonObject parameters)
    {
        var audience = parameters["audience"]?.GetValue<string>();
        if (string.IsNullOrEmpty(audience))
        {
            throw Refuse(400, "Bad Request: audience is empty");
        }

        if (!_hubGrants.TryGetValue(audience, out var granted) || granted.Length == 0)
        {
            throw Refuse(403, "Forbidden: not granted");
        }

        var asked = parameters["scopes"]?.AsArray().Select(scope => scope?.GetValue<string>()).ToHashSet();
        var given = asked is null ? granted : [.. granted.Where(asked.Contains)];
        if (given.Length == 0)
        {
            throw Refuse(403, "Forbidden: not granted");
        }

        var now = HubTime.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(10);
        return new JsonObject
        {
            ["pass"] = HubPassFormat.Sign(
                Header(),
                new JsonObject
                {
                    ["iss"] = HubIssuer,
                    ["sub"] = HubSlug,
                    ["aud"] = audience,
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["nbf"] = now.ToUnixTimeSeconds(),
                    ["exp"] = (now + lifetime).ToUnixTimeSeconds(),
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["scope"] = string.Join(' ', given),
                },
                _hubPassKeys[0].Key
            ),
            ["expires_at"] = (now + lifetime).ToUnixTimeSeconds(),
            ["scope"] = string.Join(' ', given),
        };
    }

    private HttpResponseMessage HubKeys()
    {
        if (HubKeysUnreachable)
        {
            throw new HttpRequestException("The hub's keys can't be reached, as FakeBotApi.HubKeysUnreachable asked.");
        }

        lock (_gate)
        {
            return Respond(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["issuer"] = HubIssuer,
                    ["keys"] = new JsonArray([
                        .. _hubPassKeys.Select(key => (JsonNode)HubPassFormat.Jwk(key.Id, key.Key)),
                    ]),
                }
            );
        }
    }
}
