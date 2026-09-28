using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TestUserTests
{
    [Fact]
    public async Task SendsAsync_ShouldReachTheBotFromTheUser()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/whoami");

        // Assert
        nick.LastMessage.Text.Should().Be("You are Nick");
    }

    [Fact]
    public async Task SendsAsync_ShouldAddTheMessageToTheChatBeforeTheBotsReply()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: hello");
    }

    [Fact]
    public async Task SendsAsync_WithABotCommand_ShouldMarkItTheWayTelegramDoes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/whoami please");

        // Assert
        var entity = nick.Messages[0].Message.Entities.Should().ContainSingle().Which;
        using (new AssertionScope())
        {
            entity.Type.Should().Be(MessageEntityType.BotCommand);
            entity.Offset.Should().Be(0);
            entity.Length.Should().Be("/whoami".Length);
        }
    }

    [Fact]
    public async Task SendsAsync_ByAGroupMember_ShouldReachTheBotFromThatMember()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.GroupChat("Family").Member("Anna");

        // Act
        await anna.SendsAsync("/whoami");

        // Assert
        anna.LastMessage.Text.Should().Be("You are Anna");
    }

    [Fact]
    public async Task TapsAsync_ShouldPressTheButtonTheBotSent()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("B");

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: Nick picked B");
    }

    [Fact]
    public async Task TapsAsync_ShouldPressTheButtonOnTheNewestMessageShowingIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("A");

        // Assert
        nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text).Should().Equal("Pick one", "Nick picked A");
    }

    [Fact]
    public async Task TapsAsync_OnAnOlderMessage_ShouldPressTheButtonThere()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var first = nick.LastMessage;
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("A", on: first);

        // Assert
        nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text).Should().Equal("Nick picked A", "Pick one");
    }

    [Fact]
    public async Task TapsAsync_WhenNoMessageShowsTheButton_ShouldNameTheButtonsThatAre()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var act = () => nick.TapsAsync("C");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Nick sees no \"C\" button*The buttons are \"A\", \"B\", \"Dismiss\", \"Docs\".");
    }

    [Fact]
    public async Task TapsAsync_OnALink_ShouldSayTheBotNeverHearsOfIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var act = () => nick.TapsAsync("Docs");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"Docs\"*not a callback button*");
    }

    [Fact]
    public async Task TapsAsync_OnAMessageTheBotDeleted_ShouldSaySo()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;
        await nick.TapsAsync("Dismiss");

        // Act
        var act = () => nick.TapsAsync("A", on: menu);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"Pick one\" is no longer in*");
    }
}
