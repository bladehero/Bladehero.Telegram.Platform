using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

public sealed class InlineKeyboardMarkupExtensionsTests
{
    [Fact]
    public void AddButton_ShouldAddTheEncodedButtonToTheLastRow()
    {
        // Act
        var keyboard = new InlineKeyboardMarkup()
            .AddButton("Small", new Cup(Size: 1))
            .AddButton("Large", new Cup(Size: 3));

        // Assert
        Rows(keyboard).Should().BeEquivalentTo([new[] { ("Small", "kbd-cup:1"), ("Large", "kbd-cup:3") }]);
    }

    [Fact]
    public void AddButton_AfterAddNewRow_ShouldStartTheNewRow()
    {
        // Act
        var keyboard = new InlineKeyboardMarkup()
            .AddButton("Small", new Cup(Size: 1))
            .AddNewRow()
            .AddButton("Large", new Cup(Size: 3));

        // Assert
        Rows(keyboard)
            .Should()
            .BeEquivalentTo(
                [new[] { ("Small", "kbd-cup:1") }, new[] { ("Large", "kbd-cup:3") }],
                options => options.WithStrictOrdering()
            );
    }

    private static (string Text, string? Data)[][] Rows(InlineKeyboardMarkup keyboard) =>
        [.. keyboard.InlineKeyboard.Select(row => row.Select(button => (button.Text, button.CallbackData)).ToArray())];

    [ButtonData("kbd-cup")]
    private readonly record struct Cup(int Size);
}
