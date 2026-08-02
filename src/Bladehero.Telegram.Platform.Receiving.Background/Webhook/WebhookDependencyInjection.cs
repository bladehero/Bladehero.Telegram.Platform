using System.Reflection;
using Bladehero.Configuration.Extensions;
using Microsoft.AspNetCore.Builder;
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
    public static void UseTelegramWebhook(this IEndpointRouteBuilder builder)
    {
        var configuration = builder.ServiceProvider.GetRequiredService<IOptions<TelegramWebhookConfiguration>>().Value;
        var endpoint = NormalizeEndpointPath(configuration.UpdateEndpoint);
        Console.WriteLine("[{0}]: Set bot update endpoint `{1}`", nameof(UseTelegramWebhook), endpoint);
        builder.MapPost(
            endpoint,
            async (
                Update update,
                TelegramBotClientAccessor accessor,
                IUpdateHandler handler,
                ILogger<WebhookEndpoints> logger,
                CancellationToken token
            ) =>
            {
                var client = accessor.Client;
                try
                {
                    logger.LogInformation("Received webhook update: {@Update}", update);
                    await handler.HandleUpdateAsync(client, update, token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while handling telegram update");
                    await handler.HandleErrorAsync(client, ex, HandleErrorSource.HandleUpdateError, token);
                }
            }
        );
    }

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
        services.AddHostedService<TelegramWebhookInitializer>();
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
