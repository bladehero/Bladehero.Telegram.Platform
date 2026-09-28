using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Polling;

namespace Bladehero.Telegram.Platform.Receiving;

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

        services.AddScoped<ITelegramErrorHandler, LoggingTelegramErrorHandler>();
        services.AddScoped<ITelegramCommandExecutor, ParallelTelegramCommandExecutor>();
        services.AddScoped<IUpdateHandler, ReceivingUpdateHandler>();
        services.AddTelegramCommands(assemblies);
        return services;
    }

    private static void AddTelegramCommands(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var commandTypes = assemblies
            .SelectMany(x => x.DefinedTypes)
            .Where(x => x is { IsClass: true, IsAbstract: false, IsGenericType: false })
            .Where(x => x.ImplementedInterfaces.Contains(TelegramCommandMarker))
            .Select(type => (Priority: type.GetCommandPriority(), Type: type))
            .OrderBy(x => x.Priority, CommandPriority.Comparer)
            .ToArray();

        foreach (var (_, type) in commandTypes)
        {
            services.AddScoped(type);
            services.AddScoped(TelegramCommandMarker, provider => provider.GetRequiredService(type));
        }

        services.AddScoped(provider =>
        {
            var commands = commandTypes
                .Select(x => (x.Priority, (ITelegramCommand)provider.GetRequiredService(x.Type)))
                .ToArray();
            return new CommandPriorityAccessor(commands);
        });
    }

    private static CommandPriority GetCommandPriority(this TypeInfo type)
    {
        return type.GetCustomAttribute<CommandPriorityAttribute>()?.Priority ?? CommandPriority.Default;
    }
}
