namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Marks a struct as a button's callback data: <see cref="Prefix"/>, then its constructor's fields.</summary>
/// <remarks>
/// Fields may be strings, integers, bool, <see cref="Guid"/>, enums, <see cref="DateOnly"/> or nullable ones, and
/// every settable value must be a constructor parameter; both are checked when the receiving services are added.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ButtonAttribute : Attribute
{
    private const int LongestPrefix = 32;

    /// <param name="prefix">The data's first segment: 1-32 characters of a-z, 0-9, _ and -.</param>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> breaks that rule.</exception>
    public ButtonAttribute(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix.Length is 0 or > LongestPrefix || !prefix.All(IsAllowed))
        {
            throw new ArgumentException(
                $"A button prefix is 1-{LongestPrefix} characters of a-z, 0-9, _ and -; \"{prefix}\" isn't.",
                nameof(prefix)
            );
        }

        Prefix = prefix;
    }

    /// <summary>The data's first segment, such as <c>redeem</c>.</summary>
    public string Prefix { get; }

    private static bool IsAllowed(char letter) => letter is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';
}
