namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>Keeps conversations between updates; in memory by default.</summary>
/// <remarks>
/// Register your own to persist them (it replaces the default in any registration order), or use it directly to open a
/// conversation outside an update.
/// </remarks>
public interface IConversationStore
{
    Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token);

    Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token);

    Task RemoveAsync(ConversationKey key, CancellationToken token);
}
