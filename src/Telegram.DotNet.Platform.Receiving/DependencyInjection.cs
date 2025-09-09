using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Polling;
using Telegram.DotNet.Platform.Receiving.Commands;
using Telegram.DotNet.Platform.Receiving.Commands.Execution;
using Telegram.DotNet.Platform.Receiving.Commands.Execution.Parallel;
using Telegram.DotNet.Platform.Receiving.Errors;

namespace Telegram.DotNet.Platform.Receiving;

public static class DependencyInjection
{
    private static readonly Type TelegramCommandMarker = typeof(ITelegramCommand);

    public static IServiceCollection AddTelegramReceiving(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        if (assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly is required", nameof(assemblies));
        }

        services.AddSingleton<ITelegramErrorHandler, LoggingTelegramErrorHandler>();
        services.AddSingleton<ITelegramCommandExecutor, ParallelTelegramCommandExecutor>();
        services.AddSingleton<IUpdateHandler, ReceivingUpdateHandler>();
        services.AddTelegramCommands(assemblies);
        return services;
    }

    private static void AddTelegramCommands(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var types = assemblies
            .SelectMany(x => x.DefinedTypes)
            .Where(x => x is { IsClass: true, IsAbstract: false, IsGenericType: false })
            .Where(x => x.ImplementedInterfaces.Contains(TelegramCommandMarker));

        foreach (var type in types)
        {
            services.AddScoped(TelegramCommandMarker, type);
        }
    }
}
