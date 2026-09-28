using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Receiving.Tests.CommandMenu;

public sealed class BotCommandMenuTests
{
    [Fact]
    public void Commands_ShouldListOrderedCommandsFirstThenTheRestAlphabetically()
    {
        // Arrange
        var sut = new BotCommandMenu([
            Declare<CancelCommand>("cancel", order: 100),
            Declare<StartCommand>("start", order: -1),
            Declare<HelpCommand>("help"),
            Declare<AboutCommand>("about"),
        ]);

        // Act
        var commands = sut.Commands.Select(x => x.Command);

        // Assert
        commands.Should().Equal("start", "cancel", "about", "help");
    }

    [Fact]
    public void Commands_WhenACommandHasNoOrder_ShouldFollowEvenTheHighestOrder()
    {
        // Arrange
        var sut = new BotCommandMenu([Declare<AboutCommand>("about"), Declare<HelpCommand>("help", int.MaxValue)]);

        // Act
        var commands = sut.Commands.Select(x => x.Command);

        // Assert
        commands.Should().Equal("help", "about");
    }

    [Fact]
    public void Commands_WhenNoCommandSetsAnOrder_ShouldBeAlphabetical()
    {
        // Arrange
        var sut = new BotCommandMenu([Declare<StartCommand>("start"), Declare<AboutCommand>("about")]);

        // Act
        var commands = sut.Commands.Select(x => x.Command);

        // Assert
        commands.Should().Equal("about", "start");
    }

    [Fact]
    public void Constructor_WhenTwoCommandsSetTheSameOrder_ShouldThrowNamingBoth()
    {
        // Act
        var act = () =>
            new BotCommandMenu([Declare<StartCommand>("start", order: 1), Declare<HelpCommand>("help", order: 1)]);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*order 1*StartCommand*HelpCommand*");
    }

    [Fact]
    public void Constructor_WhenTwoCommandsDeclareTheSameName_ShouldThrowNamingBoth()
    {
        // Act
        var act = () => new BotCommandMenu([Declare<HelpCommand>("help"), Declare<AboutCommand>("help")]);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*/help*HelpCommand*AboutCommand*");
    }

    [Fact]
    public void Constructor_WhenMoreCommandsAreDeclaredThanTelegramShows_ShouldThrow()
    {
        // Arrange
        var declared = Enumerable.Range(0, 101).Select(index => Declare<HelpCommand>($"command_{index}"));

        // Act
        var act = () => new BotCommandMenu(declared);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*at most 100*101*");
    }

    private static (Type, BotCommandAttribute) Declare<TCommand>(string command, int? order = null)
    {
        var entry = new BotCommandAttribute(command, $"does {command}");
        if (order is { } value)
        {
            entry.Order = value;
        }

        return (typeof(TCommand), entry);
    }

    private sealed class StartCommand;

    private sealed class HelpCommand;

    private sealed class AboutCommand;

    private sealed class CancelCommand;
}
