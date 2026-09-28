namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Keeps conversations between updates.
/// </summary>
/// <remarks>
/// The built-in store holds them in memory, so a restart forgets every conversation in progress. Register your
/// own implementation to persist them; it replaces the built-in one whether it is registered before or after
/// the receiving services. Use it directly to open a conversation outside of an update — after a background job
/// finishes, say.
/// </remarks>
public interface IConversationStore
{
    Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token);

    Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token);

    Task RemoveAsync(ConversationKey key, CancellationToken token);
}
