using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

// The button types by prefix, checked at registration; every problem is reported at once.
internal sealed class ButtonCatalog
{
    private readonly Dictionary<string, ButtonCodec> _codecs;

    private ButtonCatalog(Dictionary<string, ButtonCodec> codecs) => _codecs = codecs;

    // The [ButtonData] structs among the types, and those the commands handle.
    internal static ButtonCatalog Scan(IEnumerable<Type> types, IReadOnlyCollection<CatalogedCommand> commands) =>
        Create(
            types
                .Where(type => type.IsValueType)
                .Concat(commands.Select(command => DataOf(command.Type)).OfType<Type>())
                .Distinct()
                .Select(type => (Type: type, Button: type.GetCustomAttribute<ButtonDataAttribute>()))
                .Where(x => x.Button is not null)
                .Select(x => (x.Type, x.Button!.Prefix)),
            commands
        );

    // Explicit prefixes, for tests.
    internal static ButtonCatalog Create(
        IEnumerable<(Type Type, string Prefix)> buttons,
        IReadOnlyCollection<CatalogedCommand> commands
    )
    {
        var declared = buttons.ToArray();
        var problems = new List<string>();
        var codecs = new List<ButtonCodec>();

        foreach (var (type, prefix) in declared)
        {
            if (ButtonCodec.TryCreate(type, prefix, out var codec, out var problem))
            {
                codecs.Add(codec);
            }
            else
            {
                problems.Add(problem);
            }
        }

        foreach (var clash in declared.GroupBy(x => x.Prefix, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            problems.Add(
                $"{Names(clash.Select(x => x.Type))} {(clash.Count() == 2 ? "both" : "all")} use the prefix "
                    + $"\"{clash.Key}\"; give each button its own."
            );
        }

        var buttonTypes = declared.Select(x => x.Type).ToHashSet();
        var handlers = commands
            .Select(command => (Command: command, Data: DataOf(command.Type)))
            .Where(x => x.Data is not null)
            .ToArray();

        foreach (var (command, data) in handlers)
        {
            if (!buttonTypes.Contains(data!) && !OverridesParse(command.Type))
            {
                var name = ButtonCodec.NameOf(data!);
                problems.Add(
                    $"{ButtonCodec.NameOf(command.Type)} handles {name}, which has no [ButtonData] attribute; "
                        + $"mark {name} [ButtonData(\"prefix\")] or override Parse."
                );
            }
        }

        foreach (var button in handlers.Where(x => buttonTypes.Contains(x.Data!)).GroupBy(x => x.Data!))
        {
            var name = ButtonCodec.NameOf(button.Key);

            var regular = button.Where(x => x.Command.Step is null).Select(x => x.Command.Type).ToArray();
            if (regular.Length > 1)
            {
                problems.Add($"{name} buttons are handled by {Names(regular)}; give each button type one command.");
            }

            var steps = button.Select(x => x.Command).Where(x => x.Step is not null).ToArray();
            for (var first = 0; first < steps.Length; first++)
            {
                for (var second = first + 1; second < steps.Length; second++)
                {
                    var (one, other) = (steps[first], steps[second]);
                    if (Overlap(one.Step!, other.Step!))
                    {
                        problems.Add(
                            $"{name} buttons are handled by {Names([one.Type, other.Type])}, which can both run at "
                                + $"the same step of \"{one.Step!.Flow}\"; give each step one command for it."
                        );
                    }
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The typed buttons can't be set up:\n- " + string.Join("\n- ", problems.Order(StringComparer.Ordinal))
            );
        }

        return new ButtonCatalog(codecs.ToDictionary(x => x.Prefix, StringComparer.Ordinal));
    }

    // The codec whose prefix is the data's whole first segment.
    public bool TryFind(string data, [NotNullWhen(true)] out ButtonCodec? codec)
    {
        var end = data.IndexOf(':');
        return _codecs.TryGetValue(end < 0 ? data : data[..end], out codec);
    }

    // The TData of a CallbackQueryCommand<TData>, or null for any other command.
    private static Type? DataOf(Type command)
    {
        for (var type = command; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CallbackQueryCommand<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool OverridesParse(Type command) =>
        command
            .GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(string)])
            ?.DeclaringType
            is not { IsGenericType: true } declaring
        || declaring.GetGenericTypeDefinition() != typeof(CallbackQueryCommand<>);

    // Steps of one flow overlap when either takes every step, or both take the same one.
    private static bool Overlap(ConversationStepAttribute first, ConversationStepAttribute second) =>
        first.Flow == second.Flow && (first.Step is null || second.Step is null || first.Step == second.Step);

    // "A and B", or "A, B and C", sorted.
    private static string Names(IEnumerable<Type> types)
    {
        var names = types.Select(ButtonCodec.NameOf).Order(StringComparer.Ordinal).ToArray();
        return names.Length == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
