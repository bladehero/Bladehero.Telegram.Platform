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
    /// <param name="flow">The flow the command is a step of.</param>
    /// <param name="step">The step it runs at, or <c>null</c> for any step of the flow.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="flow"/> is blank, or <paramref name="step"/> is given but blank; thrown when the attribute is
    /// read, at startup.
    /// </exception>
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

    /// <summary>The flow the command is a step of.</summary>
    public string Flow { get; }

    /// <summary>The step the command runs at, or <c>null</c> for any step of <see cref="Flow"/>.</summary>
    public string? Step { get; }

    internal bool Matches(ConversationState state) => Flow == state.Flow && (Step is null || Step == state.Step);
}
