using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationStepAttributeTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData("order", "")]
    [InlineData("order", " ")]
    public void Constructor_WhenFlowOrStepIsBlank_ShouldThrow(string flow, string? step)
    {
        // Act
        var act = () => new ConversationStepAttribute(flow, step);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
