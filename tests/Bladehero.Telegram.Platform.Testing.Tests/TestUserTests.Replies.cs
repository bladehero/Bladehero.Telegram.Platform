using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task RepliesAsync_ShouldPostAReplyTheBotSees()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;

        // Act
        var reply = await nick.RepliesAsync(menu, "/replied");

        // Assert
        using (new AssertionScope())
        {
            reply.ReplyTo!.Id.Should().Be(menu.Id);
            reply.ToString().Should().Be("Nick (↩ Bot: Pick one): /replied");
            nick.LastMessage.ToString().Should().Be("Bot: You replied to: Pick one");
        }
    }

    [Fact]
    public async Task RepliesAsync_ToAMessageFromAnotherChat_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.PrivateChat("Anna");
        await anna.SendsAsync("/menu");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.RepliesAsync(anna.LastMessage, "hello");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("\"Pick one\" is not in the chat with Nick.");
    }

    [Fact]
    public async Task ReplyKeyboard_WhenSelective_ShouldShowOnlyForTheRepliedToUser()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        var nick = family.Member("Nick");

        // Act
        await anna.SendsAsync("/pickme");

        // Assert
        using (new AssertionScope())
        {
            anna.ReplyKeyboard!.ToString().Should().Be("[A]");
            nick.ReplyKeyboard.Should().BeNull();
            family.LastMessage.ToString().Should().Be("Bot (↩ Anna: /pickme): Pick one");
        }
    }
}
