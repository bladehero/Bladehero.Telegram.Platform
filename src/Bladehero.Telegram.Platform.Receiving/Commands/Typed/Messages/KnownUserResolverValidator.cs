using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

// The options the resolver check hangs on, validated on start.
internal sealed class KnownUserResolvers;

// Fails start when a known-user command's resolver isn't registered; without IServiceProviderIsService it can't check.
internal sealed class KnownUserResolverValidator(
    IReadOnlyDictionary<Type, string[]> commandsByUser,
    IServiceProviderIsService? services
) : IValidateOptions<KnownUserResolvers>
{
    // TUser, and the names of the commands that need its resolver, sorted.
    internal static IReadOnlyDictionary<Type, string[]> RequirementsOf(IEnumerable<Type> commands) =>
        commands
            .Select(command => (Command: command, User: UserOf(command)))
            .Where(x => x.User is not null)
            .GroupBy(x => x.User!)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Command.Name).Order(StringComparer.Ordinal).ToArray());

    public ValidateOptionsResult Validate(string? name, KnownUserResolvers options)
    {
        if (services is null)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = commandsByUser
            .Where(x => !services.IsService(typeof(ITelegramUserResolver<>).MakeGenericType(x.Key)))
            .OrderBy(x => x.Key.Name, StringComparer.Ordinal)
            .Select(x =>
                $"Commands for known {x.Key.Name} users need an ITelegramUserResolver<{x.Key.Name}>, which isn't "
                + $"registered: {string.Join(", ", x.Value)}. Register one, e.g. "
                + $"services.AddScoped<ITelegramUserResolver<{x.Key.Name}>, …>()."
            )
            .ToArray();

        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // The TUser of a KnownUserMessageCommand<TUser> or KnownUserCallbackQueryCommand<TUser, TData>.
    private static Type? UserOf(Type command)
    {
        for (var type = command.BaseType; type is not null; type = type.BaseType)
        {
            if (
                type.IsGenericType
                && type.GetGenericTypeDefinition() is var definition
                && (
                    definition == typeof(KnownUserMessageCommand<>)
                    || definition == typeof(KnownUserCallbackQueryCommand<,>)
                )
            )
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
