using Bladehero.Telegram.Platform.History;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class TelegramHistoryEntryTests
{
    [Fact]
    public void ToString_ShouldShowTheKindAndTheText()
    {
        // Arrange
        var entry = new TelegramHistoryEntry { Kind = "sendMessage", Text = "Hi" };

        // Act
        var text = entry.ToString();

        // Assert
        text.Should().Be("sendMessage: Hi");
    }

    [Fact]
    public void ToString_WithoutText_ShouldShowTheKind()
    {
        // Arrange
        var entry = new TelegramHistoryEntry { Kind = "answerCallbackQuery" };

        // Act
        var text = entry.ToString();

        // Assert
        text.Should().Be("answerCallbackQuery");
    }

    [Fact]
    public void ToString_OfARefusedCall_ShouldShowTheCodeAndTheError()
    {
        // Arrange
        var entry = new TelegramHistoryEntry
        {
            Kind = "sendMessage",
            Text = "Hi",
            ErrorCode = 403,
            Error = "Forbidden: bot was blocked by the user",
        };

        // Act
        var text = entry.ToString();

        // Assert
        text.Should().Be("sendMessage: Hi (403 Forbidden: bot was blocked by the user)");
    }

    [Fact]
    public void ToString_OfMultiLineText_ShouldStayOnOneLine()
    {
        // Arrange
        var entry = new TelegramHistoryEntry
        {
            Kind = "sendMessage",
            Text = "Your order:\nA large latte",
            ErrorCode = 400,
            Error = "Bad Request:\r\nmessage is too long",
        };

        // Act
        var text = entry.ToString();

        // Assert
        text.Should().Be("sendMessage: Your order: A large latte (400 Bad Request: message is too long)");
    }

    [Fact]
    public void ToString_OfAFailedCallWithoutACode_ShouldShowTheError()
    {
        // Arrange
        var entry = new TelegramHistoryEntry { Kind = "sendMessage", Error = "Request timed out" };

        // Act
        var text = entry.ToString();

        // Assert
        text.Should().Be("sendMessage (Request timed out)");
    }
}
