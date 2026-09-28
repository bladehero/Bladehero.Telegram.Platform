namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Where a conversation stands: the flow it runs, the step it waits at and the data gathered so far.
/// </summary>
/// <param name="Flow">The conversation's name, matched against <see cref="ConversationStepAttribute.Flow"/>.</param>
/// <param name="Step">The step it waits at, matched against <see cref="ConversationStepAttribute.Step"/>.</param>
/// <param name="Data">
/// Whatever the flow carries between steps. The <see cref="ConversationExtensions"/> helpers store it as JSON;
/// to the store it is an opaque string.
/// </param>
public sealed record ConversationState(string Flow, string Step, string? Data = null);
