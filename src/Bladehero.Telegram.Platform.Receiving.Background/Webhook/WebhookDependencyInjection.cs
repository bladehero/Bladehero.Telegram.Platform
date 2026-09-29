using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bladehero.Configuration.Extensions;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

/// <summary>Hosts the bot on a webhook: Telegram posts the updates to an endpoint of the app.</summary>
public static class WebhookDependencyInjection
{
    private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>
    /// Maps <c>POST {UpdateEndpoint}</c>, where Telegram posts the updates. With a <c>SecretToken</c> configured, a
    /// request without it gets 401 before anything is read; a body that isn't an update gets 400, and 500 means the
    /// update handler couldn't be built. Once handling has started the answer is 200, even when a command or the error
    /// handler fails, so Telegram doesn't deliver the update again.
    /// </summary>
    public static void UseTelegramWebhook(this IEndpointRouteBuilder builder)
    {
        var configuration = builder.ServiceProvider.GetRequiredService<IOptions<TelegramWebhookConfiguration>>().Value;
        var endpoint = NormalizeEndpointPath(configuration.UpdateEndpoint);
        var secretToken = configuration.HasSecretToken ? Encoding.UTF8.GetBytes(configuration.SecretToken!) : null;

        builder
            .ServiceProvider.GetRequiredService<ILogger<WebhookEndpoints>>()
            .LogInformation("Receiving Telegram updates at {Endpoint}", endpoint);

        // Nothing is read or built before the secret token checks out: not the body, not the update handler.
        builder.MapPost(
            endpoint,
            async (HttpContext context, ILogger<WebhookEndpoints> logger, CancellationToken token) =>
            {
                if (secretToken is not null && !CarriesSecretToken(context.Request, secretToken))
                {
                    logger.LogWarning("Refused a request to {Endpoint} without the webhook's secret token", endpoint);
                    return Results.Unauthorized();
                }

                if (await ReadUpdateAsync(context.Request, token) is not { } update)
                {
                    return Results.BadRequest();
                }

                // Outside the reporting below, so a handler or client that cannot be built answers 500.
                var handler = context.RequestServices.GetRequiredService<IUpdateHandler>();
                var client = context.RequestServices.GetRequiredService<ITelegramBotClient>();
                try
                {
                    // Debug, as updates carry personal data.
                    logger.LogDebug("Received webhook update: {@Update}", update);
                    await handler.HandleUpdateAsync(client, update, token);
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    await ReportAsync(context.RequestServices, new TelegramError(exception, client, update), logger);
                }
                catch (OperationCanceledException)
                {
                    // The request is gone or the app is stopping, as at the end of polling: nothing to report.
                }

                // Once handling has started, anything but 200 would only make Telegram deliver the update again.
                return Results.Ok();
            }
        );
    }

    // The error handler logs the error, as with polling; one that fails itself is logged here and goes no further.
    private static async Task ReportAsync(IServiceProvider services, TelegramError error, ILogger logger)
    {
        try
        {
            await services.GetRequiredService<ITelegramErrorHandler>().HandleAsync(error);
        }
        catch (Exception failure)
        {
            // Both stacks: the error first, then the handler's failure.
            logger.LogError(
                new AggregateException(error.Exception, failure),
                "The Telegram error handler failed on an error from update {UpdateId}",
                error.Update?.Id
            );
        }
    }

    // Null for a body that is not an update.
    private static async Task<Update?> ReadUpdateAsync(HttpRequest request, CancellationToken token)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Compared in constant time, so the response time tells nothing about the token.
    private static bool CarriesSecretToken(HttpRequest request, byte[] secretToken) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(request.Headers[SecretTokenHeader].ToString()),
            secretToken
        );

    /// <summary>
    /// Receives updates on a webhook for the commands in <paramref name="assemblies"/>, with
    /// <see cref="TelegramWebhookConfiguration"/> bound from <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// Registers the bot as <c>AddTelegramBot</c> does and the commands as <c>AddTelegramReceiving</c> does; map the
    /// endpoint with <see cref="UseTelegramWebhook"/>. Startup sets the webhook only when it changed, or on every start
    /// with a secret token; an invalid <c>SecretToken</c> fails startup.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configuration">The configuration that holds the webhook's section.</param>
    /// <param name="sectionName">
    /// The section to bind; when <c>null</c>, the one named after the type: <c>TelegramWebhookConfiguration</c>.
    /// </param>
    /// <param name="httpClientFactory">
    /// Builds the <see cref="HttpClient"/> of the client the library builds, e.g. for a proxy; it doesn't apply to a
    /// client the app registers itself.
    /// </param>
    /// <param name="assemblies">The assemblies to scan for commands.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddTelegramWebhookReceiving(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null,
        params Assembly[] assemblies
    )
    {
        services.AddConfiguration<TelegramWebhookConfiguration>(configuration, sectionName);
        services.AddTelegramWebhookReceivingCore(httpClientFactory, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook for the commands in <paramref name="assemblies"/>, with
    /// <see cref="TelegramWebhookConfiguration"/> set by <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// Registers the bot as <c>AddTelegramBot</c> does and the commands as <c>AddTelegramReceiving</c> does; map the
    /// endpoint with <see cref="UseTelegramWebhook"/>. Startup sets the webhook only when it changed, or on every start
    /// with a secret token; an invalid <c>SecretToken</c> fails startup.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets the configuration, e.g. its token and addresses.</param>
    /// <param name="assemblies">The assemblies to scan for commands.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddTelegramWebhookReceiving(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration> configure,
        params Assembly[] assemblies
    )
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook like <see cref="AddTelegramWebhookReceiving(IServiceCollection,
    /// Action{TelegramWebhookConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> resolved from the
    /// container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramWebhookReceiving<TDep1>(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration, TDep1> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook like <see cref="AddTelegramWebhookReceiving(IServiceCollection,
    /// Action{TelegramWebhookConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> and
    /// <typeparamref name="TDep2"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramWebhookReceiving<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration, TDep1, TDep2> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook like <see cref="AddTelegramWebhookReceiving(IServiceCollection,
    /// Action{TelegramWebhookConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep3"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramWebhookReceiving<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration, TDep1, TDep2, TDep3> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook like <see cref="AddTelegramWebhookReceiving(IServiceCollection,
    /// Action{TelegramWebhookConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep4"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramWebhookReceiving<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration, TDep1, TDep2, TDep3, TDep4> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates on a webhook like <see cref="AddTelegramWebhookReceiving(IServiceCollection,
    /// Action{TelegramWebhookConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep5"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramWebhookReceiving<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    private static void AddTelegramWebhookReceivingCore(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient>? httpClientFactory,
        params Assembly[] assemblies
    )
    {
        services.AddTelegramBot<IOptions<TelegramWebhookConfiguration>>(
            (bot, webhook) => bot.Token = webhook.Value.Token,
            httpClientFactory
        );
        services.AddTelegramReceiving(assemblies);
        services
            .AddOptions<TelegramWebhookConfiguration>()
            .Validate(configuration => configuration.SecretTokenIsValid, TelegramWebhookConfiguration.SecretTokenRule)
            .ValidateOnStart();
        services.AddHostedService<TelegramWebhookInitializer>();
        services.AddHostedService<TelegramCommandMenuInitializer<TelegramWebhookConfiguration>>();
    }

    /// <summary>
    /// Turns an <c>UpdateEndpoint</c> such as <c>telegram/updates</c> into the route path <c>/telegram/updates</c>;
    /// used by <see cref="UseTelegramWebhook"/>, not meant for application code.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <c>null</c>, empty or whitespace.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string NormalizeEndpointPath(this string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Endpoint path cannot be empty", nameof(path));
        }

        return $"/{path.Trim(' ', '/')}";
    }

    private sealed class WebhookEndpoints;
}
