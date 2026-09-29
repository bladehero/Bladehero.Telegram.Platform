using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

internal static class DependencyInjection
{
    private static readonly Type TelegramCommandMarker = typeof(ITelegramCommand);

    private static readonly Type[] KnownUserCommands =
    [
        typeof(KnownUserCommand<>),
        typeof(KnownUserCallbackQueryCommand<,>),
    ];

    private static readonly CommandInjectionStrategy[] InjectionStrategies =
    [
        new(
            type => KnownUserCommands.Any(type.DerivesFromOpenGeneric),
            // Built like any other command, so a missing dependency fails naming it, then given its resolver.
            (type, provider) =>
            {
                var instance = ActivatorUtilities.CreateInstance(provider, type);

                var userResolver = type.GetProperty(
                    nameof(KnownUserCommand<>.UserResolver),
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!;
                userResolver.SetValue(instance, provider.GetRequiredService(userResolver.PropertyType));

                return instance;
            }
        ),
    ];

    internal static void AddTelegramCommands(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        var commands = assemblies
            .SelectMany(x => x.DefinedTypes)
            .Where(x => x is { IsClass: true, IsAbstract: false, IsGenericType: false })
            .Where(x => x.ImplementedInterfaces.Contains(TelegramCommandMarker))
            .Select(type => new CatalogedCommand(
                type,
                type.GetCommandPriority(),
                type.GetCustomAttribute<ConversationStepAttribute>()
            ))
            .OrderBy(x => x.Priority, CommandPriority.Comparer)
            .ToArray();

        foreach (var command in commands)
        {
            AddTelegramCommand(services, command.Type);
            services.AddScoped(TelegramCommandMarker, provider => provider.GetRequiredService(command.Type));
        }

        var catalog = new CommandCatalog(commands);
        services.AddSingleton(catalog);
        services.AddScoped(provider => new CommandPriorityAccessor([
            .. catalog.Regular.Select(x => x.Resolve(provider)),
        ]));

        services.AddSingleton<IBotCommandMenu>(
            new BotCommandMenu(
                commands
                    .Select(x => (x.Type, Entry: x.Type.GetCustomAttribute<BotCommandAttribute>()))
                    .Where(x => x.Entry is not null)
                    .Select(x => (x.Type, x.Entry!))
            )
        );
    }

    private static void AddTelegramCommand(IServiceCollection services, Type type)
    {
        var strategy = InjectionStrategies.FirstOrDefault(x => x.Predicate(type));
        if (strategy is null)
        {
            services.AddScoped(type);
            return;
        }

        services.AddScoped(type, provider => strategy.Factory(type, provider));
    }

    private static CommandPriority GetCommandPriority(this TypeInfo type)
    {
        return type.GetCustomAttribute<CommandPriorityAttribute>()?.Priority ?? CommandPriority.Default;
    }

    private static bool DerivesFromOpenGeneric(this Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CommandInjectionStrategy(
        Func<Type, bool> Predicate,
        Func<Type, IServiceProvider, object> Factory
    );
}
