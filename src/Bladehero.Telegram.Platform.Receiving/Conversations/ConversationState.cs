namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>Where a conversation stands.</summary>
/// <param name="Flow">Matched against <see cref="ConversationStepAttribute.Flow"/>.</param>
/// <param name="Step">Matched against <see cref="ConversationStepAttribute.Step"/>.</param>
/// <param name="Data">
/// Data carried between steps; JSON via <see cref="ConversationExtensions"/>, opaque to the store.
/// </param>
public sealed record ConversationState(string Flow, string Step, string? Data = null);
