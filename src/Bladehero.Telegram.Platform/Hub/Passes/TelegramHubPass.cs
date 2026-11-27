namespace Bladehero.Telegram.Platform.Hub;

/// <summary>A pass from the hub that shows another app who is calling and what it was granted there.</summary>
/// <param name="Audience">The app it is for, by its slug, such as <c>familybudget</c>.</param>
/// <param name="Token">The pass itself, for <c>Authorization: Bearer</c>. Never log it.</param>
/// <param name="ExpiresAt">When it stops counting.</param>
/// <param name="Scopes">What it carries, in the callee's own words.</param>
public sealed record TelegramHubPass(
    string Audience,
    string Token,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes
)
{
    /// <summary>Names the pass without its token, so logging one by mistake gives nothing away.</summary>
    public override string ToString() => $"A hub pass for {Audience} until {ExpiresAt:O}";
}
