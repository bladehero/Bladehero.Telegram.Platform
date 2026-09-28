namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// An error Telegram answers a request with, for <see cref="FakeBotApi.Fail"/>. The bot's client throws it as an
/// <c>ApiRequestException</c> carrying the same code and description.
/// </summary>
/// <param name="ErrorCode">The HTTP status Telegram answers with, such as 400, 403 or 429.</param>
/// <param name="Description">Telegram's description, which bots often match on.</param>
public sealed record BotApiError(int ErrorCode, string Description)
{
    /// <summary>The user blocked the bot, so nothing can be sent to them.</summary>
    public static BotApiError BotBlocked { get; } = new(403, "Forbidden: bot was blocked by the user");

    public static BotApiError ChatNotFound { get; } = new(400, "Bad Request: chat not found");

    /// <summary>For a 429: the seconds to wait before trying again, which the bot's client waits out itself.</summary>
    public int? RetryAfter { get; init; }

    /// <summary>
    /// Telegram's flood limit. Telegram.Bot waits out <paramref name="retryAfter"/> seconds and retries on its own, a
    /// few times, before it gives up and throws.
    /// </summary>
    public static BotApiError TooManyRequests(int retryAfter) =>
        new(429, $"Too Many Requests: retry after {retryAfter}") { RetryAfter = retryAfter };
}
