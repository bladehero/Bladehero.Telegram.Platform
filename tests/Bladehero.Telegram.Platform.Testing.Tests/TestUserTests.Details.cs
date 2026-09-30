using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task PrivateChat_WithDetails_ShouldGiveTheBotThem()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick", "Doe", "nick_d", "uk");

        // Act
        await nick.SendsAsync("/details");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Nick Doe @nick_d uk; chat @nick_d");
            (nick.LastName, nick.Username, nick.LanguageCode).Should().Be(("Doe", "nick_d", "uk"));
        }
    }

    [Fact]
    public async Task PrivateChat_WithADetailGivenLater_ShouldKeepItAndLeaveEarlierMessagesAlone()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var before = await nick.SendsAsync("hello");

        // Act
        bot.GroupChat("Family").Member("Nick", username: "nick_d");
        await nick.SendsAsync("/details");

        // Assert
        using (new AssertionScope())
        {
            before.Message.From!.Username.Should().BeNull();
            nick.LastMessage.Text.Should().Be("Nick  @nick_d ; chat @nick_d");
        }
    }

    [Fact]
    public async Task PrivateChat_WithAConflictingUsername_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.PrivateChat("Nick", username: "nick_d");

        // Act
        var act = () => bot.PrivateChat("Nick", username: "nick_x");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Nick was first opened with username \"nick_d\", not \"nick_x\"; a name is one Telegram user "
                    + "throughout the test."
            );
    }

    [Theory]
    [InlineData("nick")]
    [InlineData("1nick_d")]
    [InlineData("nick_d_")]
    [InlineData("nick__d")]
    [InlineData("nick-d")]
    [InlineData("admin_nick")]
    [InlineData("TelegramFan")]
    [InlineData("a234567890123456789012345678901234")]
    public async Task PrivateChat_WithAnInvalidUsername_ShouldThrow(string username)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.PrivateChat("Nick", username: username);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("username")
            .Which.Message.Should()
            .StartWith(
                $"\"{username}\" isn't a username Telegram gives: 5-32 of a-z, A-Z, 0-9 and _, starting with a "
                    + "letter, without a trailing or double _, and not starting with a reserved word such as admin or "
                    + "telegram."
            );
    }

    [Theory]
    [InlineData("english")]
    [InlineData("e")]
    [InlineData("pt_br")]
    public async Task PrivateChat_WithAnInvalidLanguageCode_ShouldThrow(string languageCode)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.PrivateChat("Nick", languageCode: languageCode);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("languageCode")
            .Which.Message.Should()
            .StartWith($"\"{languageCode}\" isn't a language code such as en or pt-br.");
    }

    [Fact]
    public async Task Member_WithAUsernameAnotherUserHas_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.PrivateChat("Nick", username: "nick_d");

        // Act
        var act = () => bot.GroupChat("Family").Member("Anna", username: "Nick_D");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("The username \"Nick_D\" belongs to Nick already.");
    }
}
