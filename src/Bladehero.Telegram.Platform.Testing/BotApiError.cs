namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A Telegram error for <see cref="FakeBotApi.Fail"/>; the bot's client throws it as an <c>ApiRequestException</c>.
/// </summary>
/// <param name="ErrorCode">The HTTP status, e.g. 400, 403 or 429.</param>
/// <param name="Description">Telegram's description text.</param>
public sealed record BotApiError(int ErrorCode, string Description)
{
    /// <summary>403: the user blocked the bot.</summary>
    public static BotApiError BotBlocked { get; } = new(403, "Forbidden: bot was blocked by the user");

    /// <summary>400: Telegram doesn't know the chat, or the bot isn't in it.</summary>
    public static BotApiError ChatNotFound { get; } = new(400, "Bad Request: chat not found");

    /// <summary>400: the tap was answered already, or too late.</summary>
    public static BotApiError QueryTooOld { get; } =
        new(400, "Bad Request: query is too old and response timeout expired or query ID is invalid");

    /// <summary>For a 429: seconds to wait before retrying.</summary>
    public int? RetryAfter { get; init; }

    /// <summary>
    /// 429 flood limit; Telegram.Bot waits <paramref name="retryAfter"/> seconds and retries a few times itself.
    /// </summary>
    public static BotApiError TooManyRequests(int retryAfter) =>
        new(429, $"Too Many Requests: retry after {retryAfter}") { RetryAfter = retryAfter };
}
