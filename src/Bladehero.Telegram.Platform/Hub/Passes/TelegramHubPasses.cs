using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Hub;

// Passes kept per audience (and scopes asked), renewed once five minutes are left, one request to the hub at a time. When
// the hub fails, the last pass serves while it's still good, so a hub deploy goes unnoticed.
internal sealed class TelegramHubPasses(ITelegramBotClient client, TimeProvider time, ILogger<TelegramHubPasses> logger)
    : ITelegramHubPasses
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    // A pass closer to its end than this isn't handed out: the call it goes on may take a while.
    private static readonly TimeSpan GoodFor = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    public async ValueTask<TelegramHubPass> GetAsync(
        string audience,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        string[]? asked = scopes is null
            ? null
            : [.. scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var slot = _slots.GetOrAdd(asked is null ? audience : $"{audience} {string.Join(' ', asked)}", _ => new Slot());

        if (slot.Fresh(time.GetUtcNow()) is { } fresh)
        {
            return fresh;
        }

        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            // Whoever waited may find it renewed by the one before.
            if (slot.Fresh(time.GetUtcNow()) is { } renewed)
            {
                return renewed;
            }

            try
            {
                var answer = await client.SendRequest(
                    new GetHubPassRequest { Audience = audience, Scopes = asked },
                    cancellationToken
                );
                var now = time.GetUtcNow();
                var pass = new TelegramHubPass(
                    audience,
                    answer.Pass,
                    DateTimeOffset.FromUnixTimeSeconds(answer.ExpiresAt),
                    answer.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                );

                // Five minutes before the end, or halfway for a hub that gives short passes.
                var life = pass.ExpiresAt - now;
                slot.Keep(pass, now + (life > 2 * RenewBefore ? life - RenewBefore : life / 2));
                return pass;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (slot.Pass is { } last && last.ExpiresAt - time.GetUtcNow() > GoodFor)
                {
                    logger.LogWarning(
                        exception,
                        "The hub gave no new pass for {Audience}; the last one serves until {ExpiresAt}",
                        audience,
                        last.ExpiresAt
                    );
                    return last;
                }

                throw new TelegramHubPassUnavailableException(audience, exception);
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private sealed class Slot
    {
        private volatile Kept? _kept;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public TelegramHubPass? Pass => _kept?.Pass;

        public TelegramHubPass? Fresh(DateTimeOffset now) => _kept is { } kept && now < kept.RenewAt ? kept.Pass : null;

        public void Keep(TelegramHubPass pass, DateTimeOffset renewAt) => _kept = new Kept(pass, renewAt);

        private sealed record Kept(TelegramHubPass Pass, DateTimeOffset RenewAt);
    }
}
