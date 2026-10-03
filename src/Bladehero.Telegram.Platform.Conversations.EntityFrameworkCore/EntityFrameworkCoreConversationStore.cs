using System.Runtime.ExceptionServices;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore;

// Each call gets a context in a scope of its own, so its saves never mix with the update's unit of work.
internal sealed class EntityFrameworkCoreConversationStore<TContext>(IServiceScopeFactory scopes, TimeProvider? clock)
    : IConversationStore
    where TContext : DbContext
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var stored = await ConversationOf(ContextOf(scope), key).AsNoTracking().SingleOrDefaultAsync(token);

        return stored is null
            ? null
            : new ConversationState(stored.Flow, stored.Step, stored.Data) { Id = stored.RunId };
    }

    public async Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Another save may add the row, or a removal delete it, in between; a fresh scope then finds it as it now stands.
        if (await TrySaveAsync(key, state, race: null, token) is { } race)
        {
            await TrySaveAsync(key, state, race, token);
        }
    }

    public async Task RemoveAsync(ConversationKey key, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = ContextOf(scope);
        if (await FindAsync(context, key, token) is not { } stored)
        {
            return;
        }

        context.Remove(stored);
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed meanwhile.
        }
    }

    // Null once saved; otherwise how the first try may have met another save or a removal, for the retry.
    private async Task<Race?> TrySaveAsync(
        ConversationKey key,
        ConversationState state,
        Race? race,
        CancellationToken token
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = ContextOf(scope);
        var stored = await FindAsync(context, key, token);
        var adding = stored is null;
        if (adding && race is { Adding: true } failedAdd)
        {
            // Still no row: the add met no other save, so its own error stands.
            ExceptionDispatchInfo.Throw(failedAdd.Error);
        }

        if (stored is null)
        {
            stored = new StoredConversation { ChatId = key.ChatId, UserId = key.UserId };
            context.Add(stored);
        }

        stored.Flow = state.Flow;
        stored.Step = state.Step;
        stored.Data = state.Data;
        stored.RunId = state.Id;
        stored.UpdatedAt = _clock.GetUtcNow();

        // Even when the app's context doesn't detect changes on its own.
        context.ChangeTracker.DetectChanges();
        try
        {
            await context.SaveChangesAsync(token);
            return null;
        }
        catch (DbUpdateException exception) when (race is null && (adding || exception is DbUpdateConcurrencyException))
        {
            return new Race(exception, adding);
        }
    }

    private static TContext ContextOf(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<TContext>();

    // Tracked even when the app's context doesn't track by default, so a change to it is saved.
    private static Task<StoredConversation?> FindAsync(
        TContext context,
        ConversationKey key,
        CancellationToken token
    ) => ConversationOf(context, key).AsTracking().SingleOrDefaultAsync(token);

    private static IQueryable<StoredConversation> ConversationOf(TContext context, ConversationKey key)
    {
        var (chatId, userId) = key;
        return context.Set<StoredConversation>().Where(x => x.ChatId == chatId && x.UserId == userId);
    }

    // A first try's failure, and whether it was adding the row or updating it.
    private readonly record struct Race(DbUpdateException Error, bool Adding);
}
