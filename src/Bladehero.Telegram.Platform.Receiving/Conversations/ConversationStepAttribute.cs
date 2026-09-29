namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Makes a command a step: it runs only while the sender's conversation is in <see cref="Flow"/>, at
/// <see cref="Step"/> (any step when unset).
/// </summary>
/// <remarks>
/// Steps run before regular commands, which see the update only if every step declines. A free-text step should
/// decline bot commands so <c>/cancel</c> falls through.
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
