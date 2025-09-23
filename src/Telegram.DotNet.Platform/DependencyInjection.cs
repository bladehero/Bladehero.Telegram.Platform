using System.Net;
using System.Net.Sockets;
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

    public static IServiceCollection AddTelegramBot<TDep1>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1> configure
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore();
        return services;
    }

    public static IServiceCollection AddTelegramBot<TDep1, TDep2>(
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

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3>(
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

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4>(
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

    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4, TDep5>(
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
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(
                        context.DnsEndPoint.Host,
                        AddressFamily.InterNetwork,
                        token
                    );

                    var address = addresses[0];
                    var endpoint = new IPEndPoint(address, context.DnsEndPoint.Port);

                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(endpoint, token);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            };

            var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            return new TelegramBotClient(options, httpClient);
        });
    }
}
