namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Marks a struct as a button's callback data: <see cref="Prefix"/>, then its constructor's fields.</summary>
/// <remarks>
/// <c>[Button("redeem")] record struct Redeem(long OwnerId, int Points)</c> writes <c>redeem:123:10</c>. The fields are
/// the parameters of the one public constructor that matches the struct's properties, and each is a string, an
/// integer, a bool, a <see cref="Guid"/>, an enum or a <see cref="DateOnly"/>, or a nullable one. Prefixes, fields and
/// the commands handling each button type are checked when the receiving services are added.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ButtonAttribute : Attribute
{
    private const int LongestPrefix = 32;

    /// <param name="prefix">
    /// The first segment of the data, telling this button type from every other: 1-32 characters of a-z, 0-9, _ and -.
    /// </param>
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

    /// <summary>The first segment of the data, such as <c>redeem</c>.</summary>
    public string Prefix { get; }

    private static bool IsAllowed(char letter) => letter is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';
}
