using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Lets other apps call this one with the hub's passes, each route asking for its own scope.</summary>
public static class HubPassAuthenticationExtensions
{
    /// <summary>
    /// Adds the <see cref="HubPassDefaults.Scheme"/> scheme: a pass on <c>Authorization: Bearer</c>, checked offline
    /// against the hub's keys, and only on a request from inside the network.
    /// </summary>
    /// <remarks>
    /// It answers nothing for a bearer token that isn't shaped like a pass, so an app's own keys keep working on the same
    /// header under their own scheme. The keys are read at the start, every hour, and for a key id not seen yet; while the
    /// hub is down the last ones serve; an app whose bot isn't behind a hub reads none and takes no pass. A request that
    /// carried <c>X-Forwarded-For</c>, <c>X-Original-For</c> or <c>Forwarded</c>, or came for a public host, gets no
    /// pass in, whatever a forwarded-headers middleware did to it. A route says what it needs with
    /// <see cref="RequireHubScope{TBuilder}"/>. A pass a delegate such as Home got for a person counts only where
    /// <see cref="HubPassOptions.People"/> is set, on <see cref="HubPassOptions.PeoplePaths"/>.
    /// </remarks>
    /// <param name="builder">The app's authentication.</param>
    /// <param name="configure">Changes the defaults, which suit an app behind the hub.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static AuthenticationBuilder AddHubPasses(
        this AuthenticationBuilder builder,
        Action<HubPassOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(HubPassFormat.KeysClient, client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.TryAddSingleton<HubPassKeys>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HubPassKeysRefresh>());

        // First of all, so it notes how a request arrived before any forwarded-headers middleware rewrites it.
        if (!builder.Services.Any(service => service.ImplementationType == typeof(HubPassArrivalFilter)))
        {
            builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, HubPassArrivalFilter>());
        }

        return builder.AddScheme<HubPassOptions, HubPassAuthentication>(HubPassDefaults.Scheme, configure);
    }

    /// <summary>
    /// Requires a hub pass that carries <paramref name="scope"/>. Called again, it requires each scope named.
    /// </summary>
    /// <param name="policy">The policy, such as one an app's own keys take too.</param>
    /// <param name="scope">The scope, one this app declares in its manifest's <c>Scopes</c>.</param>
    /// <returns><paramref name="policy"/>, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireHubScope(this AuthorizationPolicyBuilder policy, string scope)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (!policy.AuthenticationSchemes.Contains(HubPassDefaults.Scheme))
        {
            policy.AddAuthenticationSchemes(HubPassDefaults.Scheme);
        }

        return policy.RequireClaim(HubPassDefaults.HubAppClaim).RequireClaim(HubPassDefaults.ScopeClaim, scope);
    }

    /// <summary>
    /// Lets this route or group in only for a hub pass carrying <paramref name="scope"/>:
    /// <c>app.MapGet("/internal/income/credits", …).RequireHubScope("credits:read")</c>. On a group and on a route in it,
    /// both are required.
    /// </summary>
    /// <remarks>
    /// Without a pass the answer is 401, and with a pass that lacks the scope, 403. A person pass is never let in here: it
    /// is a key the person made, not an app calling.
    /// </remarks>
    /// <param name="builder">The route or group.</param>
    /// <param name="scope">The scope, one this app declares in its manifest's <c>Scopes</c>.</param>
    /// <typeparam name="TBuilder">The route's or group's builder.</typeparam>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static TBuilder RequireHubScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return builder.RequireAuthorization(policy => policy.RequireHubScope(scope));
    }

    /// <summary>
    /// Whether this caller came with a hub pass carrying <paramref name="scope"/>, for a check inside one route that
    /// does several things, such as an MCP tool.
    /// </summary>
    /// <param name="principal">The request's user.</param>
    /// <param name="scope">The scope.</param>
    public static bool HasHubScope(this ClaimsPrincipal principal, string scope) =>
        principal.HubApp() is not null && principal.HasClaim(HubPassDefaults.ScopeClaim, scope);

    /// <summary>
    /// The calling app's slug when it came with a hub pass for itself, else <c>null</c>: a person pass has none, as a
    /// person's own key has none.
    /// </summary>
    /// <param name="principal">The request's user.</param>
    public static string? HubApp(this ClaimsPrincipal principal) =>
        principal.FindFirst(HubPassDefaults.HubAppClaim)?.Value;

    /// <summary>The Telegram id of the person a person pass acted for, else <c>null</c>.</summary>
    /// <param name="principal">The request's user.</param>
    public static long? HubPerson(this ClaimsPrincipal principal) =>
        long.TryParse(
            principal.FindFirst(HubPassDefaults.HubPersonClaim)?.Value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var person
        )
            ? person
            : null;

    /// <summary>The delegate a person pass came through, such as <c>home</c>, else <c>null</c>.</summary>
    /// <param name="principal">The request's user.</param>
    public static string? HubVia(this ClaimsPrincipal principal) =>
        principal.FindFirst(HubPassDefaults.HubViaClaim)?.Value;
}
