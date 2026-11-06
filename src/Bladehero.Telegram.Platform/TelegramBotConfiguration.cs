namespace Bladehero.Telegram.Platform;

/// <summary>The bot's settings for <c>AddTelegramBot</c>; the receiving configurations extend them.</summary>
public class TelegramBotConfiguration
{
    internal const string ApiBaseUrlRule =
        "ApiBaseUrl must be an http or https URL with only a host and port, such as http://hub:8080, or empty for Telegram's.";

    /// <summary>The token @BotFather gave the bot, such as <c>123456:ABC-DEF...</c>. Required.</summary>
    public required string Token { get; set; }

    /// <summary>
    /// Where the Bot API is, when it isn't Telegram's: your own Bot API server, or a service that answers for one, such
    /// as <c>http://hub:8080</c>. Empty means Telegram's.
    /// </summary>
    /// <remarks>
    /// Requests and file downloads both go there, with <see cref="Token"/> in their URLs as usual. A wrong address is
    /// reported when the client is first built, which for a receiving bot is on start. A client the app registers itself
    /// keeps its own address, and this isn't read.
    /// </remarks>
    public string? ApiBaseUrl { get; set; }

    // Empty as well as null: a blank environment variable, as a compose file leaves one, means not set.
    internal string? ApiBaseUrlIfSet => string.IsNullOrWhiteSpace(ApiBaseUrl) ? null : ApiBaseUrl;

    // Nothing but scheme, host and port: Telegram.Bot would drop the rest without a word.
    internal bool ApiBaseUrlIsValid =>
        ApiBaseUrlIfSet is null
        || Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var url)
            && url.Scheme is "https" or "http"
            && url is { AbsolutePath: "/", Query: "", Fragment: "", UserInfo: "" };
}
