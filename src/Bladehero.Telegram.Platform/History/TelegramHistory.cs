using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History;

// Reads wait briefly for what's queued, so a handler sees the update it's handling and its own earlier calls, but a
// slow or failing store can't stall the bot.
internal sealed class TelegramHistory(
    TelegramHistoryWriter writer,
    IServiceScopeFactory scopeFactory,
    TimeProvider? timeProvider = null
) : ITelegramHistory
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(2);

    // The app's clock when it registered one; the library registers none.
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
        TelegramHistoryQuery query,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Limit must be at least 1.");
        }

        if (query.MessageId is not null && query.ChatId is null)
        {
            throw new ArgumentException("A message id is unique only within its chat; set ChatId too.", nameof(query));
        }

        if (!writer.Failing)
        {
            try
            {
                await writer.FlushAsync(token).WaitAsync(LongestWait, _time, token);
            }
            catch (TimeoutException)
            {
                // Reads what's stored so far.
            }
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITelegramHistoryStore>();
        return await store.ReadAsync(query, token);
    }

    public Task FlushAsync(CancellationToken token = default) => writer.FlushAsync(token);
}
