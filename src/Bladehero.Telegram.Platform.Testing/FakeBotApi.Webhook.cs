using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The webhook the bot registers instead of polling.
public sealed partial class FakeBotApi
{
    private const int LongestSecretToken = 256;

    private static readonly TimeSpan ConflictPause = TimeSpan.FromMilliseconds(100);

    private JsonObject? _webhook;
    private long _lastWebhookUpdateId;
    private int _refusedPolls;

    /// <summary>The webhook the bot set, or <c>null</c> when it has none.</summary>
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

    internal bool RefusedPolling => Volatile.Read(ref _refusedPolls) > 0;

    internal (string Url, string? SecretToken)? Webhook()
    {
        lock (_gate)
        {
            return _webhook is null
                ? null
                : (_webhook["url"]!.GetValue<string>(), _webhook["secret_token"]?.GetValue<string>());
        }
    }

    internal (JsonObject Update, long Id) StampForWebhook(JsonObject update)
    {
        var id = Interlocked.Increment(ref _lastWebhookUpdateId);
        var stamped = update.DeepClone().AsObject();
        stamped["update_id"] = id;

        ExpectAnswerTo(stamped);
        return (stamped, id);
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

    // Telegram's rules: HTTPS on 443, 80, 88 or 8443; a secret token of up to 256 [A-Za-z0-9_-], empty for none; an
    // empty URL removes the webhook.
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

        // As in Telegram, an empty secret token is none.
        var secretToken = parameters["secret_token"]?.GetValue<string>() is { Length: > 0 } token ? token : null;
        if (secretToken?.Length > LongestSecretToken)
        {
            throw Refuse(400, "Bad Request: secret token is too long");
        }

        if (secretToken?.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') is false)
        {
            throw Refuse(400, "Bad Request: secret token contains illegal characters");
        }

        _webhook = new JsonObject
        {
            ["url"] = url,
            ["secret_token"] = secretToken,
            ["has_custom_certificate"] = parameters["certificate"] is not null,
        };
        UpdateAllowedUpdates(parameters);
        return true;
    }

    private JsonObject WebhookInfo()
    {
        var info = new JsonObject
        {
            ["url"] = _webhook?["url"]?.GetValue<string>() ?? "",
            ["has_custom_certificate"] = _webhook?["has_custom_certificate"]?.GetValue<bool>() ?? false,
            ["pending_update_count"] = 0,
        };

        if (AllowedUpdatesInfo() is { } allowedUpdates)
        {
            info["allowed_updates"] = allowedUpdates;
        }

        return info;
    }

    private JsonNode DeleteWebhook()
    {
        _webhook = null;
        return true;
    }
}
