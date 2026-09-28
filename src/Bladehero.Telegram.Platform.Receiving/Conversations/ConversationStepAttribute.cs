namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Makes a command a step of a conversation: it only runs while the sender's conversation is in
/// <see cref="Flow"/> — at <see cref="Step"/>, or at any step of the flow when no step is given.
/// </summary>
/// <remarks>
/// While a conversation is active its steps see the update before any regular command, and when a step handles
/// it the regular commands are skipped. Only when every step declines does the update fall through to them — so
/// a step accepting free text should decline bot commands, or it will swallow the <c>/cancel</c> meant to end
/// the conversation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ConversationStepAttribute : Attribute
{
    public ConversationStepAttribute(string flow, string? step = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flow);

        if (step is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(step);
        }

        Flow = flow;
        Step = step;
    }

    public string Flow { get; }

    public string? Step { get; }

    internal bool Matches(ConversationState state) => Flow == state.Flow && (Step is null || Step == state.Step);
}
