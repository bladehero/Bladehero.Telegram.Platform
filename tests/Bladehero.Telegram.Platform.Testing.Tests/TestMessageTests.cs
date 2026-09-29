using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TestMessageTests
{
    [Fact]
    public async Task ButtonsOf_ShouldDecodeTheButtonsOfThatTypeInKeyboardOrder()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/cups");

        // Assert
        nick.LastMessage.ButtonsOf<TestBot.Cups>().Should().Equal(new TestBot.Cups(1), new TestBot.Cups(2));
    }

    [Fact]
    public async Task ButtonsOf_ForANonButtonType_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/cups");

        // Act
        var act = () => nick.LastMessage.ButtonsOf<NotAButton>();

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("NotAButton isn't button data: mark it [Button(\"prefix\")].");
    }

    [Fact]
    public async Task ButtonsOf_ShouldSkipOtherData()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/menu");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Buttons.Should().NotBeEmpty();
            nick.LastMessage.ButtonsOf<TestBot.Cups>().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Document_ShouldHoldWhatTheBotUploaded()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/report");

        // Assert
        var document = nick.LastMessage.Document!;
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: (document report.csv) Your report");
            document.FileName.Should().Be("report.csv");
            document.MimeType.Should().Be("text/csv");
            document.ReadAsString().Should().Be("a,b\n1,2");
        }
    }

    [Fact]
    public async Task Photo_ShouldHoldWhatTheBotUploaded()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/photo");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: (photo) A cat [Rename] [Again]");
            nick.LastMessage.Photo!.Content.Should().Equal("jpeg bytes"u8.ToArray());
        }
    }

    [Fact]
    public async Task Voice_ShouldHoldWhatTheBotRecorded()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/say");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: (voice 2s)");
            nick.LastMessage.Voice!.ReadAsString().Should().Be("ogg bytes");
        }
    }

    [Fact]
    public async Task Photo_SentAgainById_ShouldBeTheSameFile()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/photo");
        var first = nick.LastMessage.Photo!;

        // Act
        await nick.TapsAsync("Again");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Photo!.Id.Should().Be(first.Id);
            nick.LastMessage.Photo!.Content.Should().Equal(first.Content);
        }
    }

    [Fact]
    public async Task Caption_ShouldShowTheBotsEditAndTakeAwayTheKeyboard()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/photo");

        // Act
        await nick.TapsAsync("Rename");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: (photo) A renamed cat");
            nick.LastMessage.IsEdited.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Photo_SentByUrl_ShouldNameTheUrlButHaveNoContent()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/cat");

        // Assert
        var photo = nick.LastMessage.Photo!;
        using (new AssertionScope())
        {
            photo.Url.Should().Be("https://example.com/cat.jpg");
            photo.Invoking(x => x.Content).Should().Throw<InvalidOperationException>().WithMessage("*URL*");
        }
    }

    [Fact]
    public async Task Photo_OfAUsersMessage_ShouldHoldWhatTheySent()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("lunch"u8.ToArray(), caption: "Lunch");

        // Assert
        nick.Messages[0].Photo!.Content.Should().Equal("lunch"u8.ToArray());
    }

    [Fact]
    public async Task Files_OfATextMessage_ShouldBeNull()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        var message = nick.LastMessage;
        using (new AssertionScope())
        {
            message.Photo.Should().BeNull();
            message.Document.Should().BeNull();
            message.Voice.Should().BeNull();
        }
    }

    // No [Button], so it can't be button data.
    private readonly record struct NotAButton(int Value);
}
