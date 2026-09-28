using System.Collections.Concurrent;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<ConversationKey, ConversationState> _conversations = new();

    public Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token) =>
        Task.FromResult(_conversations.GetValueOrDefault(key));

    public Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token)
    {
        _conversations[key] = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(ConversationKey key, CancellationToken token)
    {
        _conversations.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
