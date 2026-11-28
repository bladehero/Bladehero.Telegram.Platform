using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>
/// Kinds of private message an app answers when nothing else on the hub takes them, for
/// <see cref="TelegramHubConfiguration.FallbackFor"/>.
/// </summary>
[Flags]
public enum HubFallback
{
    /// <summary>None: the app gets only what it claims by command, pattern or button.</summary>
    None = 0,

    /// <summary>Text that doesn't start with <c>/</c>.</summary>
    Text = 1,

    /// <summary>A voice note.</summary>
    Voice = 2,

    /// <summary>A photo.</summary>
    Photo = 4,

    /// <summary>A file sent as a document, such as a PDF.</summary>
    Document = 8,
}

internal static class HubClaims
{
    // How the hub runs a claim pattern. NonBacktracking keeps a pattern from one app from slowing every message down, and
    // refuses what it can't run in linear time, such as back-references and look-arounds.
    internal const RegexOptions PatternOptions =
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // The hub refuses the whole manifest past these, so the app checks them at its start.
    internal const int MostPatterns = 20;
    internal const int LongestPattern = 500;

    internal const HubFallback AllFallbacks =
        HubFallback.Text | HubFallback.Voice | HubFallback.Photo | HubFallback.Document;

    internal static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > LongestPattern)
        {
            return false;
        }

        try
        {
            _ = new Regex(pattern, PatternOptions);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // The words the hub reads in fallback_for, in a fixed order.
    internal static string[] Words(HubFallback fallback) =>
        [.. Kinds.Where(kind => fallback.HasFlag(kind.Flag)).Select(kind => kind.Word)];

    private static readonly (HubFallback Flag, string Word)[] Kinds =
    [
        (HubFallback.Text, "text"),
        (HubFallback.Voice, "voice"),
        (HubFallback.Photo, "photo"),
        (HubFallback.Document, "document"),
    ];
}
