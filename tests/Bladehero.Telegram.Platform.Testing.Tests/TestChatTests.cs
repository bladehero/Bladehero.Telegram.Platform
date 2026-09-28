using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TestChatTests
{
    [Fact]
    public async Task Messages_ShouldShowTheBotsEditsAndTakeAwayTheKeyboard()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("A");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Nick picked A");
            nick.LastMessage.IsEdited.Should().BeTrue();
            nick.LastMessage.Buttons.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Messages_ShouldDropWhatTheBotDeleted()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("Dismiss");
        await nick.SendsAsync("/tidy");

        // Assert
        nick.Messages.Select(x => x.Text).Should().Equal("/menu");
    }

    [Fact]
    public async Task LastMessage_WhenTheChatIsEmpty_ShouldSaySo()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.LastMessage;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("There are no messages in the chat with Nick.");
    }

    [Fact]
    public async Task PrivateChat_WithTheSameName_ShouldBeTheSameChat()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        await bot.PrivateChat("Nick").SendsAsync("hello");

        // Act
        var again = bot.PrivateChat("Nick");

        // Assert
        again.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task GroupChat_ShouldShowEveryMemberTheSameMessages()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        var nick = family.Member("Nick");

        // Act
        await anna.SendsAsync("/whoami");

        // Assert
        using (new AssertionScope())
        {
            nick.Messages.Select(x => x.ToString()).Should().Equal("Anna: /whoami", "Bot: You are Anna");
            nick.LastMessage.Message.Chat.Title.Should().Be("Family");
        }
    }

    [Fact]
    public async Task Member_ShouldBeTheSamePersonAsInTheirPrivateChat()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var nickInFamily = bot.GroupChat("Family").Member("Nick");

        // Assert
        using (new AssertionScope())
        {
            nickInFamily.Id.Should().Be(nick.Id);
            nickInFamily.Chat.Id.Should().NotBe(nick.Chat.Id);
        }
    }

    [Fact]
    public async Task Member_OfAPrivateChat_ShouldSayOnlyGroupsHaveMembers()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.Chat.Member("Anna");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*only a group has members*");
    }
}
