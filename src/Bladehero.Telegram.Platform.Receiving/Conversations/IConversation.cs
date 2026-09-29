namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>The conversation of the user behind the current update.</summary>
/// <remarks>
/// Scoped to the update and written through at once; a change routes the <em>next</em> update. See
/// <see cref="ConversationExtensions"/> for start, move and read.
/// </remarks>
public interface IConversation
{
    /// <summary>The active conversation, or <c>null</c>.</summary>
    ValueTask<ConversationState?> GetAsync(CancellationToken token);

    /// <summary>Starts or replaces the conversation.</summary>
    /// <exception cref="InvalidOperationException">The update has no user in a chat (e.g. an inline query).</exception>
    Task SetAsync(ConversationState state, CancellationToken token);

    /// <summary>Ends the conversation, if any.</summary>
    Task EndAsync(CancellationToken token);
}
