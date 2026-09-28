using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

internal static class DependencyInjection
{
    private static readonly Type TelegramCommandMarker = typeof(ITelegramCommand);

    private static readonly CommandInjectionStrategy[] InjectionStrategies =
    [
        new(
            type => type.DerivesFromOpenGeneric(typeof(KnownUserCommand<>)),
            (type, provider) =>
            {
                var constructors = type.GetConstructors();
                if (constructors.Length != 1)
                {
                    throw new InvalidOperationException($"The type {type} must have one constructor only.");
                }

                var constructor = constructors[0];
                var parameters = constructor.GetParameters();
                var instance = constructor.Invoke(
                    parameters.Select(p => provider.GetService(p.ParameterType)).ToArray()
                );

                var userType = type.GetGenericArgumentOf(typeof(KnownUserCommand<>));
                var resolverType = typeof(ITelegramUserResolver<>).MakeGenericType(userType);
                var resolver = provider.GetRequiredService(resolverType);

                var userResolverProperty = type.GetProperty(
                    nameof(KnownUserCommand<>.UserResolver),
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!;
                userResolverProperty.SetValue(instance, resolver);

                return instance;
            }
        ),
    ];

    internal static void AddTelegramCommands(this IServiceCollection services, IEnumerable<Assembly> assemblies)
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
            AddTelegramCommand(services, type);
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

    private static void AddTelegramCommand(IServiceCollection services, TypeInfo type)
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

    private static Type GetGenericArgumentOf(this Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return current.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"The type {type} does not derive from {openGenericBase}.");
    }

    private sealed record CommandInjectionStrategy(
        Func<Type, bool> Predicate,
        Func<Type, IServiceProvider, object> Factory
    );
}
