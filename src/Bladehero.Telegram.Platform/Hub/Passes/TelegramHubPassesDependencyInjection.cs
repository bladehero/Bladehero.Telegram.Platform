using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Registers passes from the hub for an app that calls other apps.</summary>
public static class TelegramHubPassesDependencyInjection
{
    /// <summary>
    /// Registers <see cref="ITelegramHubPasses"/>, for a caller that sets the header itself, such as an MCP client that
    /// opens a session.
    /// </summary>
    /// <remarks>The bot is registered as usual, with the app's hub key as its token and the hub as its address.</remarks>
    /// <param name="services">The app's services.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTelegramHubPasses(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITelegramHubPasses, TelegramHubPasses>();
        return services;
    }

    /// <summary>
    /// Puts a pass for <paramref name="audience"/> on every request of this named client, as
    /// <c>Authorization: Bearer</c>: <c>services.AddHttpClient("expenses").AddHubPass("familybudget")</c>.
    /// </summary>
    /// <remarks>
    /// Only on this client, never on a request to an address someone typed. With no pass to be had, a request fails with
    /// <see cref="TelegramHubPassUnavailableException"/>, an <see cref="HttpRequestException"/>, before it is sent. A
    /// caller of several apps names a client per audience.
    /// </remarks>
    /// <param name="builder">The named client.</param>
    /// <param name="audience">The callee's slug, such as <c>familybudget</c>.</param>
    /// <param name="scopes">Fewer scopes than were granted, or <c>null</c> for all of them.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IHttpClientBuilder AddHubPass(
        this IHttpClientBuilder builder,
        string audience,
        IReadOnlyCollection<string>? scopes = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        builder.Services.AddTelegramHubPasses();
        return builder.AddHttpMessageHandler(provider => new HubPassMessageHandler(
            provider.GetRequiredService<ITelegramHubPasses>(),
            audience,
            scopes
        ));
    }

    private sealed class HubPassMessageHandler(
        ITelegramHubPasses passes,
        string audience,
        IReadOnlyCollection<string>? scopes
    ) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var pass = await passes.GetAsync(audience, scopes, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pass.Token);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
