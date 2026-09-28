using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Receiving.Tests.CommandMenu;

public sealed class BotCommandAttributeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Help")]
    [InlineData("/help")]
    [InlineData("help me")]
    [InlineData("hélp")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Constructor_WhenTheCommandBreaksTelegramsRules_ShouldThrow(string command)
    {
        // Act
        var act = () => new BotCommandAttribute(command, "does something");

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("command");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WhenTheDescriptionIsBlank_ShouldThrow(string description)
    {
        // Act
        var act = () => new BotCommandAttribute("help", description);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("description");
    }

    [Fact]
    public void Constructor_WhenTheDescriptionIsLongerThanTelegramAllows_ShouldThrow()
    {
        // Act
        var act = () => new BotCommandAttribute("help", new string('x', 257));

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("description");
    }

    [Fact]
    public void Constructor_AtTelegramsLimits_ShouldAcceptTheCommand()
    {
        // Act
        var act = () => new BotCommandAttribute("abcdefghijklmnopqrstuvwxyz_01234", new string('x', 256));

        // Assert
        act.Should().NotThrow();
    }
}
