using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

public sealed class ButtonDataAttributeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("redeem")]
    [InlineData("fuel_pick")]
    [InlineData("a-1")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Constructor_WithAValidPrefix_ShouldKeepIt(string prefix)
    {
        // Act
        var button = new ButtonDataAttribute(prefix);

        // Assert
        button.Prefix.Should().Be(prefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Redeem")]
    [InlineData("re:deem")]
    [InlineData("re@deem")]
    [InlineData("ré")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Constructor_WithAnInvalidPrefix_ShouldThrow(string prefix)
    {
        // Act
        var act = () => new ButtonDataAttribute(prefix);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("prefix")
            .WithMessage($"A button prefix is 1-32 characters of a-z, 0-9, _ and -; \"{prefix}\" isn't.*");
    }
}
