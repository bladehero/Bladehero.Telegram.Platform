using System.Diagnostics.CodeAnalysis;
using Bladehero.Telegram.Platform.Receiving.Commands;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands;

public sealed class CommandPriorityTests
{
    [Fact]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public void OrderBy_Always_ShouldProduceProperOrder()
    {
        // Arrange
        var priority0 = new CommandPriority(0);

        var priority1 = new CommandPriority(1);

        var priority2_0 = new CommandPriority(2, 0);
        var priority2_1 = new CommandPriority(2, 1);
        var priority2_another_1 = new CommandPriority(2, 1);
        var priority2_null = new CommandPriority(2);

        var priority3_1 = new CommandPriority(3, 1);
        var priority3_null = new CommandPriority(3);
        var priority3_another_null = new CommandPriority(3);

        var priorities = new[]
        {
            priority0,
            priority1,
            priority2_0,
            priority2_1,
            priority2_another_1,
            priority2_null,
            priority3_1,
            priority3_null,
            priority3_another_null,
        }.Shuffle();

        // Act
        var actual = priorities.Order(CommandPriority.Comparer);

        // Assert
        actual
            .Should()
            .BeEquivalentTo([
                new { Global = 0, Group = (int?)null },
                new { Global = 1, Group = (int?)null },
                new { Global = 2, Group = (int?)0 },
                new { Global = 2, Group = (int?)1 },
                new { Global = 2, Group = (int?)1 },
                new { Global = 2, Group = (int?)null },
                new { Global = 3, Group = (int?)1 },
                new { Global = 3, Group = (int?)null },
                new { Global = 3, Group = (int?)null },
                null,
            ]);
    }
}
