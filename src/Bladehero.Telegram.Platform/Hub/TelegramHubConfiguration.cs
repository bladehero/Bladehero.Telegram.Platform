namespace Bladehero.Telegram.Platform.Hub;

/// <summary>What this app tells the hub about itself on every start, for <c>AddTelegramHub</c>.</summary>
public sealed class TelegramHubConfiguration
{
    /// <summary>The app's name for people: its line in the hub's list of apps, and in the hub's messages. Required.</summary>
    public string Title { get; set; } = "";

    /// <summary>The app's site, for the hub's list of apps.</summary>
    public string? Site { get; set; }

    /// <summary>Whether updates from groups and channels come to this app. One app on a bot takes them.</summary>
    public bool TakesGroups { get; set; }

    /// <summary>Whether <c>/start &lt;this app's prefix&gt;-&lt;code&gt;</c> comes to this app, from anyone.</summary>
    public bool TakesSignInCodes { get; set; }

    /// <summary>Commands this app answers that its menu doesn't list, without the slash.</summary>
    public IReadOnlyList<string> UnlistedCommands { get; set; } = [];

    /// <summary>
    /// Whole-text regular expressions for plain text this app takes, such as a number. Text matching one app's pattern
    /// goes to that app, ahead of any <see cref="FallbackFor"/>; text two apps match is left to the hub.
    /// </summary>
    /// <remarks>
    /// A pattern must match the whole text, trimmed: the hub adds the anchors. It runs with
    /// <c>NonBacktracking | CultureInvariant | IgnoreCase</c>, so a pattern that engine can't compile fails the start.
    /// Keep it narrow, since it takes that text from every other app.
    /// </remarks>
    public IReadOnlyList<string> ClaimPatterns { get; set; } = [];

    /// <summary>
    /// Kinds of private message this app answers when no command, pattern or button sends them elsewhere. None by
    /// default.
    /// </summary>
    /// <remarks>
    /// A fallback must answer the person for real, not only hint at something else: the hub has its own line for what
    /// no app takes. A kind two apps declare is left to the hub.
    /// </remarks>
    public HubFallback FallbackFor { get; set; }

    /// <summary>
    /// What other apps may be granted here, in this app's own words, each with a label for people, such as
    /// <c>new("credits:read", "Поступления")</c>. The hub gives a caller a pass only for scopes listed here.
    /// </summary>
    /// <remarks>
    /// A scope is lower-case letters, digits and <c>: . _ -</c>, up to 64. Each route or tool that another app may call
    /// requires one, with <c>RequireHubScope</c>.
    /// </remarks>
    public IReadOnlyList<TelegramHubScope> Scopes { get; set; } = [];

    /// <summary>How often the list of people is read again, and this manifest sent again. Five minutes by default.</summary>
    public TimeSpan RefreshEvery { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the app's start waits for the first list of people. Thirty seconds by default.</summary>
    public TimeSpan StartWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How soon a failed read is tried again. Thirty seconds by default, and at most <see cref="RefreshEvery"/>.</summary>
    public TimeSpan RetryEvery { get; set; } = TimeSpan.FromSeconds(30);
}
