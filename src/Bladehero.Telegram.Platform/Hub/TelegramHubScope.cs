using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Something another app may be granted at this one, as this app declares it in its manifest.</summary>
/// <param name="Scope">The word a pass carries, such as <c>credits:read</c>.</param>
/// <param name="Label">What it is, for people who grant it, such as <c>Поступления</c>.</param>
public sealed partial record TelegramHubScope(string Scope, string Label)
{
    /// <summary>Whether <paramref name="scope"/> is a word the hub takes as a scope.</summary>
    /// <param name="scope">The word.</param>
    public static bool IsValid(string? scope) => scope is not null && Format().IsMatch(scope);

    [GeneratedRegex("^[a-z0-9][a-z0-9:._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
