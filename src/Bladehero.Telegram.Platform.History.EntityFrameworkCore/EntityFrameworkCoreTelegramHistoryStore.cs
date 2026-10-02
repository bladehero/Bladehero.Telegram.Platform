using Microsoft.EntityFrameworkCore;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore;

// Scoped: the writer and the reader resolve it in scopes of their own, so its context is never the app's unit of work.
internal sealed class EntityFrameworkCoreTelegramHistoryStore<TContext>(TContext context) : ITelegramHistoryStore
    where TContext : DbContext
{
    public async Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
    {
        context.Set<TelegramHistoryEntry>().AddRange(entries);
        await context.SaveChangesAsync(token);
        context.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
        TelegramHistoryQuery query,
        CancellationToken token
    )
    {
        var entries = context.Set<TelegramHistoryEntry>().AsNoTracking();
        if (query.ChatId is { } chatId)
        {
            entries = entries.Where(x => x.ChatId == chatId);
        }

        if (query.MessageId is { } messageId)
        {
            entries = entries.Where(x => x.MessageId == messageId);
        }

        if (query.InlineMessageId is { } inlineMessageId)
        {
            entries = entries.Where(x => x.InlineMessageId == inlineMessageId);
        }

        if (query.UpdateId is { } updateId)
        {
            entries = entries.Where(x => x.UpdateId == updateId);
        }

        if (query.Since is { } since)
        {
            entries = entries.Where(x => x.Time >= since);
        }

        if (query.BeforeId is { } beforeId)
        {
            entries = entries.Where(x => x.Id < beforeId);
        }

        var latest = await entries.OrderByDescending(x => x.Id).Take(query.Limit).ToListAsync(token);
        latest.Reverse();
        return latest;
    }
}
