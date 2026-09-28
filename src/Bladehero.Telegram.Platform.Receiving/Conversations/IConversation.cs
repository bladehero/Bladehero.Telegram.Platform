namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// The conversation of the user behind the current update.
/// </summary>
/// <remarks>
/// Scoped to the update: the store is read at most once and every change is written through straight away.
/// A change steers the <em>next</em> update — the current one keeps the routing it started with. The
/// <see cref="ConversationExtensions"/> helpers cover the usual start, move and read.
/// </remarks>
public interface IConversation
{
    /// <summary>
    /// The active conversation, or <c>null</c> when there is none.
    /// </summary>
    ValueTask<ConversationState?> GetAsync(CancellationToken token);

    /// <summary>
    /// Starts the conversation, or replaces the one in progress.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The update has no user in a chat to hold a conversation with — an inline query or a channel post.
    /// </exception>
    Task SetAsync(ConversationState state, CancellationToken token);

    /// <summary>
    /// Ends the conversation. Does nothing when there is none.
    /// </summary>
    Task EndAsync(CancellationToken token);
}
