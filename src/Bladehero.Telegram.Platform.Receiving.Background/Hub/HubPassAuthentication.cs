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
// to the app's other schemes, so its keys and passes share one header.
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

        // Passes that act for a person come later, from callers allowed to ask for them; until then they're refused.
        if (pass.Claims.ContainsKey("person"))
        {
            return AuthenticateResult.Fail("A pass that acts for a person isn't taken here.");
        }

        var principal = HubPassPrincipal.For(
            caller,
            Text(pass.Claims, "name"),
            (Text(pass.Claims, "scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Options.ClientIds.TryGetValue(caller, out var known) ? known : HubPassDefaults.ClientIdOf(caller)
        );
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
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

// One shape for every caller: what an app's own key gives (client_id, client_name, scope), plus hub_app. A pass that acts
// for a person will add the person to the same principal.
internal static class HubPassPrincipal
{
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
