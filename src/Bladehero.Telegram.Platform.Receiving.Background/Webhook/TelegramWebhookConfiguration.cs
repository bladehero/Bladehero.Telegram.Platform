namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

/// <summary>The settings of a webhook bot: the receiver's, plus where Telegram posts the updates.</summary>
public sealed class TelegramWebhookConfiguration : TelegramReceiverConfiguration
{
    internal const string SecretTokenRule =
        "SecretToken must be 1-256 characters of A-Z, a-z, 0-9, _ and -, or empty for none.";

    internal const string BaseUrlRule =
        "BaseUrl must be an absolute http or https URL, such as https://bot.example.com.";

    private const int LongestSecretToken = 256;

    /// <summary>The bot's public origin, such as <c>https://bot.example.com</c>. Required.</summary>
    /// <remarks>
    /// A path is kept only with a trailing <c>/</c>; the endpoint is mapped at <c>/{UpdateEndpoint}</c>.
    /// </remarks>
    public required string BaseUrl { get; set; }

    /// <summary>The path Telegram posts the updates to, such as <c>telegram/updates</c>. Required.</summary>
    public required string UpdateEndpoint { get; set; }

    /// <summary>
    /// Sent to Telegram with the webhook, which sends it back with every update; other requests to the endpoint get
    /// 401. 1-256 characters of A-Z, a-z, 0-9, _ and -; empty or <c>null</c> for none.
    /// </summary>
    public string? SecretToken { get; set; }

    /// <summary>
    /// The webhook's address: <see cref="BaseUrl"/> and <see cref="UpdateEndpoint"/> combined, such as
    /// <c>https://bot.example.com/telegram/updates</c>.
    /// </summary>
    /// <remarks>
    /// A path is kept only with a trailing <c>/</c>; the endpoint is mapped at <c>/{UpdateEndpoint}</c>.
    /// </remarks>
    public Uri WebhookUri => new(new Uri(BaseUrl), UpdateEndpoint);

    internal bool BaseUrlIsValid =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) && url.Scheme is "https" or "http";

    internal bool HasSecretToken => !string.IsNullOrEmpty(SecretToken);

    internal bool SecretTokenIsValid =>
        !HasSecretToken
        || SecretToken!.Length <= LongestSecretToken
            && SecretToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
