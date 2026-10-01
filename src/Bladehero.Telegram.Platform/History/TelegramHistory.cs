using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History;

// Reads flush first, so a handler sees the update it's handling and its own earlier calls.
internal sealed class TelegramHistory(TelegramHistoryWriter writer, IServiceScopeFactory scopeFactory)
    : ITelegramHistory
{
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

        await writer.FlushAsync(token);
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITelegramHistoryStore>();
        return await store.ReadAsync(query, token);
    }

    public Task FlushAsync(CancellationToken token = default) => writer.FlushAsync(token);
}
