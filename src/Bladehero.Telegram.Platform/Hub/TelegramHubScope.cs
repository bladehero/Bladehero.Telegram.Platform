using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Something another app, or a person through a delegate app, may be granted at this one.</summary>
/// <param name="Scope">The word a pass carries, such as <c>credits:read</c>.</param>
/// <param name="Label">What it is, for people who grant it, such as <c>Поступления</c>.</param>
/// <param name="ForPeople">
/// Whether a person may grant it to a delegate app such as Home, which then acts for them here with a person pass. Off by
/// default: a word for apps (such as <c>credits:read</c>) never rides a person pass.
/// </param>
public sealed partial record TelegramHubScope(string Scope, string Label, bool ForPeople = false)
{
    /// <summary>A scope for apps only, as before people could grant scopes.</summary>
    /// <param name="scope">The word a pass carries.</param>
    /// <param name="label">What it is, for people who grant it.</param>
    public TelegramHubScope(string scope, string label)
        : this(scope, label, ForPeople: false) { }

    /// <summary>Whether <paramref name="scope"/> is a word the hub takes as a scope.</summary>
    /// <param name="scope">The word.</param>
    public static bool IsValid(string? scope) => scope is not null && Format().IsMatch(scope);

    [GeneratedRegex("^[a-z0-9][a-z0-9:._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
