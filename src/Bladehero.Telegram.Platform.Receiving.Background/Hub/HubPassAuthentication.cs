using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Hub;

// Checks a pass on Authorization: Bearer, in the plan's order: the door, the algorithm, the key, the signature, the issuer,
// the audience, the time. What it may do is the route's to check, by scope. Anything that isn't shaped like a pass is left
// to the app's other schemes, so its keys and passes share one header. A pass naming a person is checked further, then
// made into the principal the app's own People callback gives.
internal sealed class HubPassAuthentication(
    IOptionsMonitor<HubPassOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    HubPassKeys keys,
    IOptions<TelegramBotConfiguration> bot,
    IOptions<TelegramHubConfiguration> hub,
    TimeProvider time
) : AuthenticationHandler<HubPassOptions>(options, logger, encoder)
{
    private const string Bearer = "Bearer ";

    // A person pass's own claims: whom it acts for, and the connection at the delegate it came through.
    private const string PersonClaim = "person";
    private const string ConnectionClaim = "cnx";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[Bearer.Length..].Trim();
        if (!HubPassFormat.LooksLikeAPass(token))
        {
            return AuthenticateResult.NoResult();
        }

        if (CameThroughTheEdge())
        {
            return AuthenticateResult.Fail("A hub pass counts only inside the network.");
        }

        if (!HubPassFormat.TryRead(token, out var pass))
        {
            return AuthenticateResult.Fail("The hub pass is malformed.");
        }

        if (
            Text(pass.Header, "alg") != HubPassFormat.Algorithm
            || Text(pass.Header, "typ") != HubPassFormat.Type
            || Text(pass.Header, "kid") is not { Length: > 0 } id
        )
        {
            return AuthenticateResult.Fail("The hub pass isn't signed ES256 with a hub key.");
        }

        if (!keys.HasHub)
        {
            return AuthenticateResult.Fail("This app isn't behind a hub, so no pass is taken.");
        }

        var (set, key) = await keys.FindAsync(id, Context.RequestAborted);
        if (key is not { } publicKey)
        {
            return AuthenticateResult.Fail("The hub pass is signed with a key the hub doesn't list.");
        }

        using (var ecdsa = ECDsa.Create(publicKey))
        {
            if (pass.Signature.Length != 64 || !ecdsa.VerifyData(pass.Signed, pass.Signature, HashAlgorithmName.SHA256))
            {
                return AuthenticateResult.Fail("The hub pass's signature is wrong.");
            }
        }

        var issuer = Options.Issuer ?? set?.Issuer;
        if (issuer is null || Text(pass.Claims, "iss") != issuer)
        {
            return AuthenticateResult.Fail("The hub pass is from another hub.");
        }

        var audience = Options.Audience ?? TelegramHubClient.SlugOf(bot.Value.Token ?? "");
        if (audience is null)
        {
            Logger.LogError(
                "A hub pass came, but this app has no slug to check it against: set HubPassOptions.Audience"
            );
            return AuthenticateResult.Fail("This app doesn't know its own slug.");
        }

        if (Text(pass.Claims, "aud") != audience)
        {
            return AuthenticateResult.Fail("The hub pass is for another app.");
        }

        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var skew = (long)Options.ClockSkew.TotalSeconds;
        if (
            Number(pass.Claims, "exp") is not { } expires
            || expires + skew <= now
            || Number(pass.Claims, "nbf") is { } notBefore && notBefore - skew > now
        )
        {
            return AuthenticateResult.Fail("The hub pass has run out, or isn't good yet.");
        }

        if (Text(pass.Claims, "sub") is not { Length: > 0 } caller)
        {
            return AuthenticateResult.Fail("The hub pass names no caller.");
        }

        // A delegate acts only for a person, and only a delegate does: the two kinds of pass never stand in for each other.
        var isDelegate = Options.Delegates.Contains(caller, StringComparer.Ordinal);
        if (pass.Claims.ContainsKey(PersonClaim))
        {
            return await PersonAsync(pass, caller, isDelegate);
        }

        if (isDelegate)
        {
            return AuthenticateResult.Fail("A delegate's pass acts for a person, and this one names none.");
        }

        var principal = HubPassPrincipal.For(
            caller,
            Text(pass.Claims, "name"),
            (Text(pass.Claims, "scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Options.ClientIds.TryGetValue(caller, out var known) ? known : HubPassDefaults.ClientIdOf(caller)
        );
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    // The app's own callback makes the principal its own key would give the person, from what's left after these checks.
    private async Task<AuthenticateResult> PersonAsync(ReadPass pass, string caller, bool isDelegate)
    {
        if (Options.People is not { } people)
        {
            return AuthenticateResult.Fail("This app takes no pass that acts for a person.");
        }

        if (!isDelegate)
        {
            return AuthenticateResult.Fail(
                "The hub pass acts for a person, and its caller isn't one this app lets do that."
            );
        }

        if (!Options.PeoplePaths.Any(path => Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
        {
            return AuthenticateResult.Fail("A pass that acts for a person doesn't count on this path.");
        }

        if (Number(pass.Claims, PersonClaim) is not { } person || person <= 0)
        {
            return AuthenticateResult.Fail("The hub pass names no person.");
        }

        if (
            pass.Claims[ConnectionClaim] is not JsonObject connection
            || !Guid.TryParseExact(Text(connection, "id"), "D", out var connectionId)
            || connectionId == Guid.Empty
            || Text(connection, "name") is not { } connectionName
            || !TelegramHubPersonConnection.IsName(connectionName)
        )
        {
            return AuthenticateResult.Fail("The hub pass names no connection it came through.");
        }

        // What people may grant here, as this app says it: a hub that gave more doesn't widen it.
        var forPeople = hub
            .Value.Scopes.Where(scope => scope.ForPeople)
            .Select(scope => scope.Scope)
            .ToHashSet(StringComparer.Ordinal);
        string[] scopes =
        [
            .. (Text(pass.Claims, "scope") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(forPeople.Contains)
                .Distinct(StringComparer.Ordinal),
        ];
        if (scopes.Length == 0)
        {
            return AuthenticateResult.Fail("The hub pass carries nothing a person may grant here.");
        }

        var given = await people(
            new HubPassPerson(
                person,
                caller,
                HubPassDefaults.ConnectionIdOf(caller, connectionId),
                connectionName,
                scopes,
                Context.RequestServices
            ),
            Context.RequestAborted
        );
        if (given is null)
        {
            return AuthenticateResult.Fail("This app doesn't know the person the hub pass acts for.");
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(HubPassPrincipal.ForPerson(given, person, caller, scopes), Scheme.Name)
        );
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    // The edge Caddy always says whom it passes on for. Whether it did is read as the request arrived, before any
    // forwarded-headers middleware hid it, and as it is now. A request for the app's public host came through the edge
    // too, whatever its headers say.
    private bool CameThroughTheEdge()
    {
        var arrival = Context.Features.Get<HubPassArrival>();
        if (arrival?.Forwarded == true || HubPassArrival.IsForwarded(Request.Headers))
        {
            return true;
        }

        return IsPublic(Request.Host.Host) || arrival is not null && IsPublic(arrival.Host);
    }

    private bool IsPublic(string host) =>
        Options.PublicHosts.Any(name => string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
        || Uri.TryCreate(hub.Value.Site, UriKind.Absolute, out var site)
            && string.Equals(site.Host, host, StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Number(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
}

// One shape for every caller: what an app's own key gives (client_id, client_name, scope), plus hub_app. A person pass
// gives the principal the app's own callback made, as the person's key would, plus hub_person and hub_via: never hub_app,
// which means an app acting for itself.
internal static class HubPassPrincipal
{
    public static ClaimsPrincipal ForPerson(
        ClaimsPrincipal given,
        long person,
        string via,
        IReadOnlyList<string> scopes
    )
    {
        List<ClaimsIdentity> identities =
        [
            .. given.Identities.Select(identity => new ClaimsIdentity(
                identity.Claims.Where(claim =>
                    !claim.Type.StartsWith("hub_", StringComparison.Ordinal)
                    && (
                        claim.Type != HubPassDefaults.ScopeClaim || scopes.Contains(claim.Value, StringComparer.Ordinal)
                    )
                ),
                identity.AuthenticationType ?? HubPassDefaults.Scheme,
                identity.NameClaimType,
                identity.RoleClaimType
            )),
        ];
        if (identities.Count == 0)
        {
            identities.Add(new ClaimsIdentity(HubPassDefaults.Scheme));
        }

        identities[0]
            .AddClaim(
                new Claim(
                    HubPassDefaults.HubPersonClaim,
                    person.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64
                )
            );
        identities[0].AddClaim(new Claim(HubPassDefaults.HubViaClaim, via));
        return new ClaimsPrincipal(identities);
    }

    public static ClaimsPrincipal For(string caller, string? name, IEnumerable<string> scopes, Guid clientId) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(HubPassDefaults.ClientIdClaim, clientId.ToString()),
                    new Claim(HubPassDefaults.ClientNameClaim, string.IsNullOrWhiteSpace(name) ? caller : name),
                    new Claim(HubPassDefaults.HubAppClaim, caller),
                    .. scopes
                        .Distinct(StringComparer.Ordinal)
                        .Select(scope => new Claim(HubPassDefaults.ScopeClaim, scope)),
                ],
                HubPassDefaults.Scheme,
                HubPassDefaults.ClientNameClaim,
                roleType: null
            )
        );
}
