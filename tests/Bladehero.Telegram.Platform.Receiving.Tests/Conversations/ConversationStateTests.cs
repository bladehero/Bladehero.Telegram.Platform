using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationStateTests
{
    [Fact]
    public void NewId_ShouldGiveEightLowercaseLettersOrDigits()
    {
        // Act
        var ids = Enumerable.Range(0, 100).Select(_ => ConversationState.NewId()).ToArray();

        // Assert
        using (new AssertionScope())
        {
            ids.Should().AllSatisfy(id => id.Should().MatchRegex("^[a-z0-9]{8}$"));
            ids.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void Equality_WithoutAnId_ShouldBeAsBefore()
    {
        // Arrange
        var state = new ConversationState("order", "size", "{}");

        // Act
        var (flow, step, data) = state;

        // Assert
        using (new AssertionScope())
        {
            state.Should().Be(new ConversationState("order", "size", "{}"));
            state.Id.Should().BeNull();
            (flow, step, data).Should().Be(("order", "size", "{}"));
            state.Should().NotBe(state with { Id = "k3j9x2ab" });
        }
    }
}
