using Bladehero.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
    {
        services.AddConfiguration<TelegramBotConfiguration>(configuration, sectionName);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        Action<TelegramBotConfiguration> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    private static void AddTelegramBotCore(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient>? httpClientFactory
    )
    {
        services.TryAddSingleton(provider =>
        {
            var botConfiguration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
            var options = new TelegramBotClientOptions(botConfiguration.Token);
            var client = httpClientFactory?.Invoke(provider);
            return new TelegramBotClientAccessor(new TelegramBotClient(options, client));
        });
        services.TryAddSingleton<ITelegramSender, TelegramSender>();
    }
}
