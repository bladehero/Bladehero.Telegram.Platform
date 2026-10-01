namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>Keeps conversations between updates; in memory by default.</summary>
/// <remarks>
/// Register your own to persist them (it replaces the default in any registration order), or use it directly to open a
/// conversation outside an update.
/// </remarks>
public interface IConversationStore
{
    /// <summary>The conversation stored for <paramref name="key"/>, or <c>null</c> when there is none.</summary>
    Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token);

    /// <summary>Stores <paramref name="state"/> for <paramref name="key"/>, replacing any conversation there.</summary>
    Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token);

    /// <summary>Removes the conversation of <paramref name="key"/>; does nothing when there is none.</summary>
    Task RemoveAsync(ConversationKey key, CancellationToken token);
}
