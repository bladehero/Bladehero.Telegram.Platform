using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands;

public sealed class CommandPriorityAttributeTests
{
    [Fact]
    public void Attribute_WithAGlobalPriorityOnly_ShouldLeaveTheGroupOut()
    {
        // Act
        var priority = typeof(Ungrouped).GetCustomAttribute<CommandPriorityAttribute>()!.Priority;

        // Assert
        using (new AssertionScope())
        {
            priority.Global.Should().Be(2);
            priority.Group.Should().BeNull();
        }
    }

    [Fact]
    public void Attribute_WithAGroup_ShouldSetBoth()
    {
        // Act
        var priority = typeof(Grouped).GetCustomAttribute<CommandPriorityAttribute>()!.Priority;

        // Assert
        using (new AssertionScope())
        {
            priority.Global.Should().Be(1);
            priority.Group.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Attribute_WithANegativePriority_ShouldThrow(int global, int group)
    {
        // Act
        var act = () => new CommandPriorityAttribute(global, group);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [CommandPriority(2)]
    private sealed class Ungrouped;

    [CommandPriority(1, 0)]
    private sealed class Grouped;
}
