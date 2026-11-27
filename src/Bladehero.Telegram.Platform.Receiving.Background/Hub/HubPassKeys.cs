using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Hub;

internal sealed record HubKeySet(string? Issuer, IReadOnlyDictionary<string, ECParameters> Keys);

// The hub's public keys: read at the start, every hour, and for a key id not seen yet at most every 30 seconds. A failed
// read keeps the last good set, so a callee keeps checking passes while the hub is down. An app whose bot isn't behind a
// hub, as on a developer's PC, has none to read and asks nobody.
internal sealed class HubPassKeys(
    IHttpClientFactory http,
    IOptionsMonitor<HubPassOptions> options,
    IOptions<TelegramBotConfiguration> bot,
    IEnumerable<HubKeysAddress> addresses,
    TimeProvider time,
    ILogger<HubPassKeys> logger
)
{
    private const string TelegramHost = "api.telegram.org";

    private readonly HubKeysAddress? _address = addresses.LastOrDefault();
    private readonly SemaphoreSlim _reading = new(1, 1);
    private volatile HubKeySet? _set;
    private DateTimeOffset? _lastRead;

    // Set by the first failed read of an outage and cleared by the next good one, so an outage is warned about once.
    private bool _failing;

    public HubKeySet? Current => _set;

    /// <summary>Whether there is a hub to read the keys from: a <c>KeysUrl</c>, or a bot whose API isn't Telegram's.</summary>
    public bool HasHub => KeysUrl() is not null;

    private HubPassOptions Options => options.Get(HubPassDefaults.Scheme);

    /// <summary>The key with this id, reading the set again if it's new and the last read wasn't just now.</summary>
    public async Task<(HubKeySet? Set, ECParameters? Key)> FindAsync(string id, CancellationToken cancellationToken)
    {
        if (_set is { } known && known.Keys.TryGetValue(id, out var key))
        {
            return (known, key);
        }

        await _reading.WaitAsync(cancellationToken);
        try
        {
            if (
                _set?.Keys.ContainsKey(id) != true
                && (_lastRead is not { } last || time.GetUtcNow() - last >= Options.KeysRetryEvery)
            )
            {
                await ReadCoreAsync(cancellationToken);
            }
        }
        finally
        {
            _reading.Release();
        }

        return _set is { } set && set.Keys.TryGetValue(id, out var found) ? (set, found) : (_set, null);
    }

    public async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            _reading.Release();
        }
    }

    private async Task<bool> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (KeysUrl() is not { } url)
        {
            return false;
        }

        _lastRead = time.GetUtcNow();
        try
        {
            using var client = http.CreateClient(HubPassFormat.KeysClient);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            var keys = new Dictionary<string, ECParameters>(StringComparer.Ordinal);
            foreach (var jwk in json?["keys"]?.AsArray() ?? [])
            {
                if (
                    jwk?["kty"]?.GetValue<string>() == "EC"
                    && jwk["crv"]?.GetValue<string>() == "P-256"
                    && jwk["kid"]?.GetValue<string>() is { Length: > 0 } id
                    && jwk["x"]?.GetValue<string>() is { } x
                    && jwk["y"]?.GetValue<string>() is { } y
                )
                {
                    var key = HubPassFormat.PublicKey(Base64Url.DecodeFromChars(x), Base64Url.DecodeFromChars(y));
                    key.Validate();
                    keys[id] = key;
                }
            }

            if (keys.Count == 0)
            {
                Failed("no keys in the set");
                return false;
            }

            _set = new HubKeySet(json?["issuer"]?.GetValue<string>(), keys);
            if (_failing)
            {
                _failing = false;
                logger.LogInformation("The hub's keys can be read again");
            }

            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Failed(exception.GetType().Name);
            return false;
        }
    }

    // Once per outage, with only the error's type: the hub being down is one event, not one every 30 seconds.
    private void Failed(string why)
    {
        if (_failing)
        {
            logger.LogDebug("Still can't read the hub's keys ({Error})", why);
            return;
        }

        _failing = true;
        logger.LogWarning(
            "Failed to read the hub's keys ({Error}); {State}. Said once until they can be read again",
            why,
            _set is null ? "passes are refused until they come" : "the last ones stay"
        );
    }

    private Uri? KeysUrl()
    {
        if (Options.KeysUrl is { } set)
        {
            return set;
        }

        if (_address is not null)
        {
            return _address.Url;
        }

        return
            Uri.TryCreate(bot.Value.ApiBaseUrlIfSet, UriKind.Absolute, out var hub)
            && !string.Equals(hub.Host, TelegramHost, StringComparison.OrdinalIgnoreCase)
            ? new Uri(hub, HubPassFormat.KeysPath)
            : null;
    }
}

// Reads the keys at the start without holding it up, then keeps them fresh: every hour, or every 30 seconds while there
// are none. Without a hub it does nothing.
internal sealed class HubPassKeysRefresh(
    HubPassKeys keys,
    IOptionsMonitor<HubPassOptions> options,
    TimeProvider time,
    ILogger<HubPassKeysRefresh> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!keys.HasHub)
        {
            logger.LogInformation("The bot isn't behind a hub, so no hub keys are read and no pass is taken");
            return;
        }

        try
        {
            while (true)
            {
                var read = await keys.ReadAsync(stoppingToken);
                var settings = options.Get(HubPassDefaults.Scheme);
                await Task.Delay(
                    read || keys.Current is not null ? settings.KeysRefreshEvery : settings.KeysRetryEvery,
                    time,
                    stoppingToken
                );
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }
}
