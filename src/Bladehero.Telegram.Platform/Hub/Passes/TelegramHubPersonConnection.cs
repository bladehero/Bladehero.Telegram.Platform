namespace Bladehero.Telegram.Platform.Hub;

/// <summary>
/// One of a person's connections at a delegate app such as Home: what a person pass says it came through, so the callee
/// signs what it writes with that connection.
/// </summary>
/// <param name="Id">Its fixed id at the delegate.</param>
/// <param name="Name">Its name for people, such as <c>Claude · Ник</c>: 1 to 100 characters, none of them control ones.</param>
public sealed record TelegramHubPersonConnection(Guid Id, string Name)
{
    /// <summary>The longest name a connection may have.</summary>
    public const int LongestName = 100;

    /// <summary>Whether the hub takes this connection: an id, and a name as <see cref="Name"/> says.</summary>
    public bool IsValid => Id != Guid.Empty && IsName(Name);

    /// <summary>Whether <paramref name="name"/> may name a connection.</summary>
    /// <param name="name">The name.</param>
    public static bool IsName(string? name) =>
        name is { Length: > 0 and <= LongestName } && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);
}
