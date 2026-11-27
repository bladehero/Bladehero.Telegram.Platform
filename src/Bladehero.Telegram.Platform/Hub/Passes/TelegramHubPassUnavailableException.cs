namespace Bladehero.Telegram.Platform.Hub;

/// <summary>
/// The hub gave no pass and no cached one is still good. An <see cref="HttpRequestException"/>, so a call through a client
/// with <c>AddHubPass</c> fails into the same path as the callee being out of reach.
/// </summary>
public sealed class TelegramHubPassUnavailableException : HttpRequestException
{
    /// <summary>A pass for <paramref name="audience"/> couldn't be had, for the reason in <paramref name="inner"/>.</summary>
    /// <param name="audience">The callee's slug.</param>
    /// <param name="inner">What the hub answered, or how reaching it failed.</param>
    public TelegramHubPassUnavailableException(string audience, Exception? inner)
        : base($"The hub gave no pass for {audience}: {inner?.Message ?? "no answer"}", inner) => Audience = audience;

    // The list of apps a delegate may reach: no audience to name.
    internal TelegramHubPassUnavailableException(Exception? inner)
        : base($"The hub gave no list of apps: {inner?.Message ?? "no answer"}", inner) => Audience = "";

    /// <summary>The callee's slug; empty when it was the list of apps that couldn't be had.</summary>
    public string Audience { get; }
}
