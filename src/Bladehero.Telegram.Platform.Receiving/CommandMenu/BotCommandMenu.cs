using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.CommandMenu;

internal sealed class BotCommandMenu : IBotCommandMenu
{
    internal const int MostCommands = 100;

    public BotCommandMenu(IEnumerable<(Type Command, BotCommandAttribute Entry)> declared)
    {
        var entries = declared.ToArray();

        ThrowOnClash(entries.GroupBy(x => x.Entry.Command), command => $"/{command}");
        ThrowOnClash(entries.Where(x => x.Entry.HasOrder).GroupBy(x => x.Entry.Order), order => $"order {order}");

        if (entries.Length > MostCommands)
        {
            throw new InvalidOperationException(
                $"Telegram shows at most {MostCommands} commands in the menu, but {entries.Length} are declared."
            );
        }

        var ordered = entries.Where(x => x.Entry.HasOrder).OrderBy(x => x.Entry.Order);
        var unordered = entries.Where(x => !x.Entry.HasOrder).OrderBy(x => x.Entry.Command, StringComparer.Ordinal);

        Commands =
        [
            .. ordered
                .Concat(unordered)
                .Select(x => new BotCommand { Command = x.Entry.Command, Description = x.Entry.Description }),
        ];
    }

    public IReadOnlyList<BotCommand> Commands { get; }

    private static void ThrowOnClash<TKey>(
        IEnumerable<IGrouping<TKey, (Type Command, BotCommandAttribute Entry)>> groups,
        Func<TKey, string> describe
    )
    {
        if (groups.FirstOrDefault(x => x.Count() > 1) is not { } clash)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The bot command menu has {describe(clash.Key)} more than once: {string.Join(", ", clash.Select(x => x.Command.Name))}."
        );
    }
}
