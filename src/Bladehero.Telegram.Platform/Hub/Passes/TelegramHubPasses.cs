using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Hub;

// Passes kept per audience (and scopes asked), renewed once five minutes are left, one request to the hub at a time. When
// the hub fails, the last pass serves while it's still good, so a hub deploy goes unnoticed. A person pass is kept per
// person and connection too, and a refusal ends it at once: the hub said that person may no longer do this.
internal sealed class TelegramHubPasses(ITelegramBotClient client, TimeProvider time, ILogger<TelegramHubPasses> logger)
    : ITelegramHubPasses,
        ITelegramHubPersonPasses
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    // A pass closer to its end than this isn't handed out: the call it goes on may take a while.
    private static readonly TimeSpan GoodFor = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<SlotKey, Slot> _slots = new();

    public ValueTask<TelegramHubPass> GetAsync(
        string audience,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var asked = Sorted(scopes);
        return GetAsync(
            new SlotKey(audience, Joined(asked), Person: null, Connection: null, ConnectionName: null),
            new GetHubPassRequest { Audience = audience, Scopes = asked },
            cancellationToken
        );
    }

    public ValueTask<TelegramHubPass> GetAsync(
        string audience,
        long person,
        TelegramHubPersonConnection connection,
        IReadOnlyCollection<string>? scopes = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsValid)
        {
            throw new ArgumentException(
                "A connection has an id and a name of 1 to 100 characters, none of them control ones.",
                nameof(connection)
            );
        }

        var asked = Sorted(scopes);
        return GetAsync(
            new SlotKey(audience, Joined(asked), person, connection.Id, connection.Name),
            new GetHubPassRequest
            {
                Audience = audience,
                Scopes = asked,
                Person = person,
                Connection = new HubConnectionDto { Id = connection.Id.ToString("D"), Name = connection.Name },
            },
            cancellationToken
        );
    }

    public async ValueTask<IReadOnlyList<TelegramHubApp>> GetAppsAsync(
        long person,
        CancellationToken cancellationToken = default
    )
    {
        HubAppDto[] apps;
        try
        {
            apps = await client.SendRequest(new GetHubAppsRequest { Person = person }, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TelegramHubPassUnavailableException(exception);
        }

        return
        [
            .. apps.Select(app => new TelegramHubApp(
                app.Slug,
                string.IsNullOrWhiteSpace(app.Title) ? app.Slug : app.Title,
                app.Site,
                [.. app.Scopes.Select(scope => new TelegramHubScope(scope.Scope, scope.Label, ForPeople: true))]
            )),
        ];
    }

    public void Forget(Guid connectionId)
    {
        foreach (var key in _slots.Keys.Where(key => key.Connection == connectionId))
        {
            _slots.TryRemove(key, out _);
        }
    }

    private async ValueTask<TelegramHubPass> GetAsync(
        SlotKey key,
        GetHubPassRequest request,
        CancellationToken cancellationToken
    )
    {
        var slot = _slots.GetOrAdd(key, _ => new Slot());

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
                var answer = await client.SendRequest(request, cancellationToken);
                var now = time.GetUtcNow();
                var pass = new TelegramHubPass(
                    key.Audience,
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
                if (key.Person is not null && exception is ApiRequestException { ErrorCode: >= 400 and < 500 })
                {
                    // The hub refused this person: the pass it gave before isn't handed out again.
                    _slots.TryRemove(new KeyValuePair<SlotKey, Slot>(key, slot));
                    throw new TelegramHubPassUnavailableException(key.Audience, exception);
                }

                if (slot.Pass is { } last && last.ExpiresAt - time.GetUtcNow() > GoodFor)
                {
                    logger.LogWarning(
                        exception,
                        "The hub gave no new pass for {Audience}; the last one serves until {ExpiresAt}",
                        key.Audience,
                        last.ExpiresAt
                    );
                    return last;
                }

                throw new TelegramHubPassUnavailableException(key.Audience, exception);
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private static string[]? Sorted(IReadOnlyCollection<string>? scopes) =>
        scopes is null ? null : [.. scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string? Joined(string[]? scopes) => scopes is null ? null : string.Join(' ', scopes);

    // An app's own pass has no person; a person's is theirs alone, and its connection's name goes in the pass.
    private sealed record SlotKey(
        string Audience,
        string? Scopes,
        long? Person,
        Guid? Connection,
        string? ConnectionName
    );

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
