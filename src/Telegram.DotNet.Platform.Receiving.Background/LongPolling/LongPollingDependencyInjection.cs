using System.Reflection;
using Microsoft.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Telegram.DotNet.Platform.Receiving.Background.LongPolling;

public static class LongPollingDependencyInjection
{
    public static IServiceCollection AddTelegramLongPollingReceiving(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null,
        params Assembly[] assemblies
    )
    {
        services.AddConfiguration<TelegramReceiverConfiguration>(configuration, sectionName);
        services.AddTelegramBot(configuration, sectionName ?? nameof(TelegramReceiverConfiguration), httpClientFactory);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration> configure,
        params Assembly[] assemblies
    )
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3, TDep4> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(assemblies);
        return services;
    }

    private static void AddTelegramLongPollingReceivingCore(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        services.AddTelegramReceiving(assemblies);
        services.AddHostedService<TelegramLongPollingBackgroundService>();
    }
}
