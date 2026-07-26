namespace Bladehero.Telegram.Platform.Receiving.Background.Webhook;

public sealed class TelegramWebhookConfiguration : TelegramReceiverConfiguration
{
    public required string BaseUrl { get; set; }

    public required string UpdateEndpoint { get; set; }

    public Uri WebhookUri => new(new Uri(BaseUrl), UpdateEndpoint);
}
