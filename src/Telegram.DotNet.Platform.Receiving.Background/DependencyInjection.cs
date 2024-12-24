using Microsoft.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Telegram.DotNet.Platform.Receiving.Background;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramReceivingBackground(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null
    )
    {
        services.AddConfiguration<ReceiverConfiguration>(configuration, sectionName);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground(
        this IServiceCollection services,
        Action<ReceiverConfiguration> configure
    )
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1>(
        this IServiceCollection services,
        Action<ReceiverConfiguration, TDep1> configure
    )
        where TDep1 : class
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2>(
        this IServiceCollection services,
        Action<ReceiverConfiguration, TDep1, TDep2> configure
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<ReceiverConfiguration, TDep1, TDep2, TDep3> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<ReceiverConfiguration, TDep1, TDep2, TDep3, TDep4> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<ReceiverConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<ReceiverConfiguration>().Configure(configure);
        services.AddTelegramReceivingBackgroundCore();
        return services;
    }

    private static void AddTelegramReceivingBackgroundCore(this IServiceCollection services)
    {
        services.AddSingleton<IReceiverOptionsMapper, ReceiverOptionsMapper>();
        services.AddTransient<IReceiverOptionsProvider, ReceiverOptionsProvider>();
        services.AddTelegramReceiving();
        services.AddHostedService<TelegramReceivingService>();
    }
}
