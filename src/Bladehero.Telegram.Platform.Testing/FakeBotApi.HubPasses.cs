using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Hub;

namespace Bladehero.Telegram.Platform.Testing;

// A hub's passes: getHubPass (and getHubApps, for a delegate) for the app under test as a caller, IssueHubPass and
// IssuePersonPass for it as a callee, and /hub/keys for its verifier, which TelegramTestHost points here.
public sealed partial class FakeBotApi
{
    /// <summary>The issuer of every pass this fake signs, as a hub's <c>PublicUrl</c>.</summary>
    public const string HubIssuer = "https://hub.test";

    private readonly Dictionary<string, string[]> _hubGrants = new(StringComparer.Ordinal);
    private readonly Dictionary<(long Person, string Audience), string[]> _personGrants = [];
    private readonly Dictionary<string, (string Title, string? Site, TelegramHubScope[] Scopes)> _hubAppsDescribed =
        new(StringComparer.Ordinal);
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
    ) => SignHubPass(Header(), Claims(caller, audience, scopes, issuedAt, lifetime));

    /// <summary>
    /// The apps that may ask for person passes, as the hub's <c>Hub__Delegates</c>: <c>home</c> by default. With
    /// <see cref="HubSlug"/> among them, <c>getHubPass</c> gives passes only for a person and <c>getHubApps</c> answers;
    /// otherwise both refuse a person, as a hub does.
    /// </summary>
    public ICollection<string> HubDelegates { get; } = new HashSet<string>(StringComparer.Ordinal) { "home" };

    /// <summary>
    /// Lets the app under test, a delegate, have person passes for <paramref name="person"/> at
    /// <paramref name="audience"/> with <paramref name="scopes"/>: what the hub would give after the person's list, the
    /// owner's cap and the audience's scopes for people. Without it, <c>getHubPass</c> answers 403 <c>not granted</c>.
    /// </summary>
    /// <param name="person">The person's Telegram id.</param>
    /// <param name="audience">The callee's slug.</param>
    /// <param name="scopes">The scopes the person's passes there may carry.</param>
    public void GrantPersonPass(long person, string audience, params string[] scopes)
    {
        lock (_gate)
        {
            _personGrants[(person, audience)] = scopes;
        }
    }

    /// <summary>
    /// The title, site and scope labels <c>getHubApps</c> gives for <paramref name="slug"/>. An app not described is
    /// answered with its slug as title and its words as labels.
    /// </summary>
    /// <param name="slug">The app's slug.</param>
    /// <param name="title">Its title in the hub.</param>
    /// <param name="site">Its site.</param>
    /// <param name="scopes">Its scopes, for their labels.</param>
    public void DescribeHubApp(string slug, string title, string? site = null, params TelegramHubScope[] scopes)
    {
        lock (_gate)
        {
            _hubAppsDescribed[slug] = (title, site, scopes);
        }
    }

    /// <summary>
    /// A person pass as the hub would give the delegate <paramref name="caller"/> for <paramref name="audience"/>, acting
    /// for <paramref name="person"/> through <paramref name="connection"/>, valid for ten minutes from
    /// <see cref="HubTime"/>'s now: for a request to the app under test as a callee.
    /// </summary>
    /// <param name="caller">The delegate's slug, such as <c>home</c>.</param>
    /// <param name="audience">The app under test's slug.</param>
    /// <param name="person">The person's Telegram id.</param>
    /// <param name="connection">The person's connection at the delegate.</param>
    /// <param name="scopes">What the pass carries.</param>
    public string IssuePersonPass(
        string caller,
        string audience,
        long person,
        TelegramHubPersonConnection connection,
        params string[] scopes
    ) => IssuePersonPass(caller, audience, person, connection, scopes, HubTime.GetUtcNow(), TimeSpan.FromMinutes(10));

    /// <summary>A person pass as the other overload gives, issued and lasting as said.</summary>
    /// <param name="caller">The delegate's slug.</param>
    /// <param name="audience">The app under test's slug.</param>
    /// <param name="person">The person's Telegram id.</param>
    /// <param name="connection">The person's connection at the delegate.</param>
    /// <param name="scopes">What the pass carries.</param>
    /// <param name="issuedAt">When it was issued.</param>
    /// <param name="lifetime">How long it lasts.</param>
    public string IssuePersonPass(
        string caller,
        string audience,
        long person,
        TelegramHubPersonConnection connection,
        IEnumerable<string> scopes,
        DateTimeOffset issuedAt,
        TimeSpan lifetime
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        var claims = Claims(caller, audience, scopes, issuedAt, lifetime);
        AddPerson(claims, person, connection);
        return SignHubPass(Header(), claims);
    }

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

        var person = NumberOf(parameters["person"]);
        var delegated = HubDelegates.Contains(HubSlug);
        if (parameters.ContainsKey("person") != delegated)
        {
            throw Refuse(
                403,
                delegated
                    ? "Forbidden: this app acts only for a person"
                    : "Forbidden: this app may not act for a person"
            );
        }

        TelegramHubPersonConnection? connection = null;
        string[]? granted;
        if (delegated)
        {
            connection = ConnectionOf(parameters["connection"]);
            if (person is not > 0 || connection is null)
            {
                throw Refuse(400, "Bad Request: the person or the connection is malformed");
            }

            granted = _personGrants.GetValueOrDefault((person.Value, audience));
        }
        else
        {
            granted = _hubGrants.GetValueOrDefault(audience);
        }

        if (granted is not { Length: > 0 })
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
        var claims = Claims(HubSlug, audience, given, now, lifetime);
        if (connection is not null)
        {
            AddPerson(claims, person!.Value, connection);
        }

        return new JsonObject
        {
            ["pass"] = SignHubPass(Header(), claims),
            ["expires_at"] = (now + lifetime).ToUnixTimeSeconds(),
            ["scope"] = string.Join(' ', given),
        };
    }

    private static JsonObject Claims(
        string caller,
        string audience,
        IEnumerable<string> scopes,
        DateTimeOffset issuedAt,
        TimeSpan lifetime
    ) =>
        new()
        {
            ["iss"] = HubIssuer,
            ["sub"] = caller,
            ["aud"] = audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = (issuedAt + lifetime).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["scope"] = string.Join(' ', scopes),
        };

    private static void AddPerson(JsonObject claims, long person, TelegramHubPersonConnection connection)
    {
        claims["person"] = person;
        claims["cnx"] = new JsonObject { ["id"] = connection.Id.ToString("D"), ["name"] = connection.Name };
    }

    private static TelegramHubPersonConnection? ConnectionOf(JsonNode? node)
    {
        if (
            node is not JsonObject connection
            || connection["id"] is not JsonValue id
            || !id.TryGetValue<string>(out var text)
            || !Guid.TryParseExact(text, "D", out var parsed)
            || connection["name"] is not JsonValue name
            || !name.TryGetValue<string>(out var named)
        )
        {
            return null;
        }

        var read = new TelegramHubPersonConnection(parsed, named);
        return read.IsValid ? read : null;
    }

    private JsonArray GetHubApps(JsonObject parameters)
    {
        if (!HubDelegates.Contains(HubSlug))
        {
            throw Refuse(403, "Forbidden: this app may not act for a person");
        }

        var person = NumberOf(parameters["person"]);
        if (person is not > 0)
        {
            throw Refuse(400, "Bad Request: person is malformed");
        }

        return
        [
            .. _personGrants
                .Where(grant => grant.Key.Person == person && grant.Value.Length > 0)
                .OrderBy(grant => grant.Key.Audience, StringComparer.Ordinal)
                .Select(grant =>
                {
                    var (title, site, labels) = _hubAppsDescribed.TryGetValue(grant.Key.Audience, out var described)
                        ? described
                        : (grant.Key.Audience, null, []);
                    return (JsonNode)
                        new JsonObject
                        {
                            ["slug"] = grant.Key.Audience,
                            ["title"] = title,
                            ["site"] = site,
                            ["scopes"] = new JsonArray([
                                .. grant.Value.Select(scope =>
                                    (JsonNode)
                                        new JsonObject
                                        {
                                            ["scope"] = scope,
                                            ["label"] =
                                                Array.Find(labels, known => known.Scope == scope)?.Label ?? scope,
                                        }
                                ),
                            ]),
                        };
                }),
        ];
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
