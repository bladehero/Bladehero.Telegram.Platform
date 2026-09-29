namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

public sealed class TelegramWebhookConfiguration : TelegramReceiverConfiguration
{
    internal const string SecretTokenRule =
        "SecretToken must be 1-256 characters of A-Z, a-z, 0-9, _ and -, or empty for none.";

    private const int LongestSecretToken = 256;

    public required string BaseUrl { get; set; }

    public required string UpdateEndpoint { get; set; }

    /// <summary>
    /// Sent to Telegram with the webhook, which sends it back with every update; other requests to the endpoint get
    /// 401. 1-256 characters of A-Z, a-z, 0-9, _ and -; empty or <c>null</c> for none.
    /// </summary>
    public string? SecretToken { get; set; }

    public Uri WebhookUri => new(new Uri(BaseUrl), UpdateEndpoint);

    internal bool HasSecretToken => !string.IsNullOrEmpty(SecretToken);

    internal bool SecretTokenIsValid =>
        !HasSecretToken
        || SecretToken!.Length <= LongestSecretToken
            && SecretToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
