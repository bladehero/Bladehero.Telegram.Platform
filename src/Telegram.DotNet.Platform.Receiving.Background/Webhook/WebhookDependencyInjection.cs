using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Telegram.DotNet.Platform.Receiving.Background.Webhook;

public static class WebhookDependencyInjection
{
    public static void UseTelegramWebhook(this IEndpointRouteBuilder builder)
    {
        var configuration = builder.ServiceProvider.GetRequiredService<IOptions<TelegramWebhookConfiguration>>().Value;
        var endpoint = NormalizeEndpointPath(configuration.UpdateEndpoint);
        builder.MapPost(
            endpoint,
            async (Update update, ITelegramBotClient client, IUpdateHandler handler, CancellationToken token) =>
            {
                try
                {
                    await handler.HandleUpdateAsync(client, update, token);
                }
                catch (Exception ex)
                {
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
        services.AddTelegramBot(configuration, sectionName ?? nameof(TelegramWebhookConfiguration), httpClientFactory);
        services.AddTelegramWebhookReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramWebhookReceiving(
        this IServiceCollection services,
        Action<TelegramWebhookConfiguration> configure,
        params Assembly[] assemblies
    )
    {
        services.AddOptions<TelegramWebhookConfiguration>().Configure(configure);
        services.AddTelegramWebhookReceivingCore(assemblies);
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
        services.AddTelegramWebhookReceivingCore(assemblies);
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
        services.AddTelegramWebhookReceivingCore(assemblies);
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
        services.AddTelegramWebhookReceivingCore(assemblies);
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
        services.AddTelegramWebhookReceivingCore(assemblies);
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
        services.AddTelegramWebhookReceivingCore(assemblies);
        return services;
    }

    private static void AddTelegramWebhookReceivingCore(this IServiceCollection services, params Assembly[] assemblies)
    {
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
}
