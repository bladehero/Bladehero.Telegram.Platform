namespace Bladehero.Telegram.Platform.Receiving.CommandMenu;

/// <summary>Lists the command in the bot's menu, shown when the user types <c>/</c>.</summary>
/// <remarks>
/// Ordered commands come first (lowest first, orders unique), then the rest alphabetically. Telegram's rules are
/// checked at startup, so a bad entry fails the host instead of the call to Telegram.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BotCommandAttribute : Attribute
{
    private const int LongestCommand = 32;
    private const int LongestDescription = 256;

    private int? _order;

    /// <param name="command">The command without its slash: 1–32 lowercase letters, digits or underscores.</param>
    /// <param name="description">What the menu says about it: 1–256 characters.</param>
    public BotCommandAttribute(string command, string description)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (command.Length is 0 or > LongestCommand || !command.All(IsAllowed))
        {
            throw new ArgumentException(
                $"'{command}' is not a bot command: use 1-{LongestCommand} lowercase letters, digits or underscores, without the slash.",
                nameof(command)
            );
        }

        if (description.Length > LongestDescription)
        {
            throw new ArgumentException(
                $"The description of /{command} is longer than {LongestDescription} characters.",
                nameof(description)
            );
        }

        Command = command;
        Description = description;
    }

    public string Command { get; }

    public string Description { get; }

    /// <summary>Position in the menu, lowest first; unset (<see cref="int.MaxValue"/>) goes after ordered commands.</summary>
    public int Order
    {
        get => _order ?? int.MaxValue;
        set => _order = value;
    }

    internal bool HasOrder => _order.HasValue;

    private static bool IsAllowed(char letter) => letter is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
}
