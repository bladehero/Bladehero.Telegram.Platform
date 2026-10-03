namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore;

/// <summary>A conversation as the EF Core store keeps it: one row per user in a chat.</summary>
public sealed class StoredConversation
{
    /// <summary>The chat.</summary>
    public long ChatId { get; internal set; }

    /// <summary>The user in the chat.</summary>
    public long UserId { get; internal set; }

    /// <summary>The flow, as in <c>ConversationState.Flow</c>.</summary>
    public string Flow { get; internal set; } = "";

    /// <summary>The step, as in <c>ConversationState.Step</c>.</summary>
    public string Step { get; internal set; } = "";

    /// <summary>The data, as in <c>ConversationState.Data</c>.</summary>
    public string? Data { get; internal set; }

    /// <summary>The run, as in <c>ConversationState.Id</c>.</summary>
    public string? RunId { get; internal set; }

    /// <summary>When it was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }
}
