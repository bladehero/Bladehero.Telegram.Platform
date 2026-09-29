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

public static class WebhookDependencyInjection
{
    private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>
    /// Maps <c>POST {UpdateEndpoint}</c>, where Telegram posts the updates. With a <c>SecretToken</c> configured, a
    /// request without it gets 401 before anything is read; a body that isn't an update gets 400.
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
            async (
                HttpContext context,
                TelegramBotClientAccessor accessor,
                ILogger<WebhookEndpoints> logger,
                CancellationToken token
            ) =>
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

                // Outside the reporting below, so a handler that cannot be built answers 500.
                var handler = context.RequestServices.GetRequiredService<IUpdateHandler>();
                var client = accessor.Client;
                try
                {
                    // Debug, as updates carry personal data.
                    logger.LogDebug("Received webhook update: {@Update}", update);
                    await handler.HandleUpdateAsync(client, update, token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while handling telegram update");
                    var errorHandler = context.RequestServices.GetRequiredService<ITelegramErrorHandler>();
                    await errorHandler.HandleAsync(new TelegramError(ex, client, update));
                }

                return Results.Ok();
            }
        );
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
