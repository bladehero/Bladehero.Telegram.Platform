using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Tests;

public sealed class TelegramMessageRefTests
{
    private const long Group = -1001234567890;

    [Theory]
    [InlineData(0, 10)]
    [InlineData(Group, 0)]
    [InlineData(Group, -1)]
    public void New_WithChatZeroOrMessageBelowOne_ShouldThrow(long chatId, int messageId)
    {
        // Act
        var act = () => new TelegramMessageRef(chatId, messageId);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void New_WithABlankInlineId_ShouldThrow(string inlineMessageId)
    {
        // Act
        var act = () => new TelegramMessageRef(inlineMessageId);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    public static TheoryData<TelegramMessageRef, bool> Shapes =>
        new()
        {
            { new TelegramMessageRef(Group, 10), false },
            { new TelegramMessageRef(Group, 10), true },
            { new TelegramMessageRef("AAAAinline"), false },
            { new TelegramMessageRef("AAAAinline"), true },
        };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Json_ShouldRoundTripBothShapes(TelegramMessageRef reference, bool web)
    {
        // Arrange
        var options = web ? JsonSerializerOptions.Web : JsonSerializerOptions.Default;

        // Act
        var alone = JsonSerializer.Deserialize<TelegramMessageRef>(
            JsonSerializer.Serialize(reference, options),
            options
        );
        var inRecord = JsonSerializer.Deserialize<Stored>(
            JsonSerializer.Serialize(new Stored(reference), options),
            options
        );

        // Assert
        using (new AssertionScope())
        {
            alone.Should().Be(reference);
            inRecord.Should().Be(new Stored(reference));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"ChatId":0,"MessageId":10,"InlineMessageId":null}""")]
    [InlineData("""{"ChatId":-1001234567890,"MessageId":0}""")]
    [InlineData("""{"InlineMessageId":" "}""")]
    public void Json_ThatNamesNoMessage_ShouldThrow(string json)
    {
        // Act
        var act = () => JsonSerializer.Deserialize<TelegramMessageRef>(json);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_Message_ShouldTakeItsChatAndId()
    {
        // Arrange
        var message = new Message
        {
            Id = 10,
            Chat = new Chat { Id = Group, Type = ChatType.Supergroup },
        };

        // Act
        var reference = TelegramMessageRef.From(message);

        // Assert
        reference.Should().Be(new TelegramMessageRef(Group, 10));
    }

    [Fact]
    public void From_ATapOnAMessage_ShouldTakeItsChatAndId()
    {
        // Arrange
        var tap = new CallbackQuery
        {
            Id = "cb1",
            Message = new Message
            {
                Id = 10,
                Chat = new Chat { Id = Group, Type = ChatType.Supergroup },
            },
        };

        // Act
        var reference = TelegramMessageRef.From(tap);

        // Assert
        reference.Should().Be(new TelegramMessageRef(Group, 10));
    }

    [Fact]
    public void From_ATapOnAnInlineMessage_ShouldTakeItsInlineId()
    {
        // Arrange
        var tap = new CallbackQuery { Id = "cb2", InlineMessageId = "AAAAinline" };

        // Act
        var reference = TelegramMessageRef.From(tap);

        // Assert
        using (new AssertionScope())
        {
            reference.Should().Be(new TelegramMessageRef("AAAAinline"));
            reference.ChatId.Should().Be(0);
            reference.MessageId.Should().Be(0);
        }
    }

    [Fact]
    public void From_ATapWithoutAMessage_ShouldThrow()
    {
        // Arrange
        var tap = new CallbackQuery { Id = "cb3", GameShortName = "coffee_quest" };

        // Act
        var act = () => TelegramMessageRef.From(tap);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("The tap has no message to change, as for a game button.*");
    }

    private sealed record Stored(TelegramMessageRef Card);
}
