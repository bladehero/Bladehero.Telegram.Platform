using Microsoft.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Telegram.DotNet.Platform;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null
    )
    {
        services.AddConfiguration<TelegramBotConfiguration>(configuration, sectionName);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        Action<TelegramBotConfiguration> configure
    )
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1> configure
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2> configure
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramReceivingBackground<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    private static void AddTelegramBotCore(this IServiceCollection services)
    {
        services.TryAddSingleton<ITelegramBotClient>(provider =>
        {
            var botConfiguration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
            var options = new TelegramBotClientOptions(botConfiguration.Token);
            return new TelegramBotClient(options);
        });
    }
}
