using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Hub;

internal sealed record HubKeySet(string? Issuer, IReadOnlyDictionary<string, ECParameters> Keys);

// The hub's public keys: read at the start, every hour, and for a key id not seen yet at most every 30 seconds. A failed
// read keeps the last good set, so a callee keeps checking passes while the hub is down.
internal sealed class HubPassKeys(
    IHttpClientFactory http,
    IOptionsMonitor<HubPassOptions> options,
    IOptions<TelegramBotConfiguration> bot,
    TimeProvider time,
    ILogger<HubPassKeys> logger
)
{
    private readonly SemaphoreSlim _reading = new(1, 1);
    private volatile HubKeySet? _set;
    private DateTimeOffset? _lastRead;

    public HubKeySet? Current => _set;

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
        _lastRead = time.GetUtcNow();
        try
        {
            using var client = http.CreateClient(HubPassFormat.KeysClient);
            using var response = await client.GetAsync(KeysUrl(), cancellationToken);
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
                logger.LogWarning("The hub's key set has no keys; the last one stays");
                return false;
            }

            _set = new HubKeySet(json?["issuer"]?.GetValue<string>(), keys);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Failed to read the hub's keys ({Error}); {State}",
                exception.GetType().Name,
                _set is null ? "passes are refused until they come" : "the last ones stay"
            );
            return false;
        }
    }

    private Uri KeysUrl()
    {
        if (Options.KeysUrl is { } set)
        {
            return set;
        }

        var hub = bot.Value.ApiBaseUrlIfSet ?? "https://api.telegram.org";
        return new Uri(new Uri(hub), HubPassFormat.KeysPath);
    }
}

// Reads the keys at the start without holding it up, then keeps them fresh: every hour, or every 30 seconds while there
// are none.
internal sealed class HubPassKeysRefresh(HubPassKeys keys, IOptionsMonitor<HubPassOptions> options, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
