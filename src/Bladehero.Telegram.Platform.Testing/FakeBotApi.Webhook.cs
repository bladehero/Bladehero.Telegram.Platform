using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The webhook the bot registers: where Telegram delivers its updates instead of answering getUpdates.
public sealed partial class FakeBotApi
{
    private JsonObject? _webhook;
    private long _lastWebhookUpdateId;

    /// <summary>
    /// The address the bot asked Telegram to deliver its updates to, or <c>null</c> when it has none and polls
    /// instead.
    /// </summary>
    public string? WebhookUrl
    {
        get
        {
            lock (_gate)
            {
                return _webhook?["url"]?.GetValue<string>();
            }
        }
    }

    internal (string Url, string? SecretToken)? Webhook()
    {
        lock (_gate)
        {
            return _webhook is null
                ? null
                : (_webhook["url"]!.GetValue<string>(), _webhook["secret_token"]?.GetValue<string>());
        }
    }

    // Numbers an update for delivery to the webhook, as Telegram numbers every update it sends.
    internal JsonObject StampForWebhook(JsonObject update)
    {
        var stamped = update.DeepClone().AsObject();
        stamped["update_id"] = Interlocked.Increment(ref _lastWebhookUpdateId);

        ExpectAnswerTo(stamped);
        return stamped;
    }

    private bool HasWebhook
    {
        get
        {
            lock (_gate)
            {
                return _webhook is not null;
            }
        }
    }

    // Telegram delivers only over HTTPS, on ports 443, 80, 88 or 8443. An empty URL removes the webhook.
    private JsonNode SetWebhook(JsonObject parameters)
    {
        var url = parameters["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(url))
        {
            _webhook = null;
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw Refuse(400, "Bad Request: bad webhook: An HTTPS URL must be provided for webhook");
        }

        if (uri.Port is not (443 or 80 or 88 or 8443))
        {
            throw Refuse(400, "Bad Request: bad webhook: Webhook can be set up only on ports 80, 88, 443 or 8443");
        }

        // Left out, allowed_updates keeps the list the bot gave before, as the Bot API documents.
        _webhook = new JsonObject
        {
            ["url"] = url,
            ["secret_token"] = parameters["secret_token"]?.DeepClone(),
            ["allowed_updates"] = (parameters["allowed_updates"] ?? _webhook?["allowed_updates"])?.DeepClone(),
        };
        return true;
    }

    private JsonObject WebhookInfo()
    {
        var info = new JsonObject
        {
            ["url"] = _webhook?["url"]?.GetValue<string>() ?? "",
            ["has_custom_certificate"] = false,
            ["pending_update_count"] = 0,
        };

        if (_webhook?["allowed_updates"] is { } allowedUpdates)
        {
            info["allowed_updates"] = allowedUpdates.DeepClone();
        }

        return info;
    }

    private JsonNode DeleteWebhook()
    {
        _webhook = null;
        return true;
    }
}
