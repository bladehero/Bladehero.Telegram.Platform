using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task LastReply_ShouldBeTheNewestBotMessageEvenAfterAUserWrites()
    {
        // Arrange: no command answers /nothing.
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        await nick.SendsAsync("/nothing");

        // Assert
        using (new AssertionScope())
        {
            nick.LastReply.Text.Should().Be("Pick one");
            nick.LastMessage.Text.Should().Be("/nothing");
        }
    }

    [Fact]
    public async Task LastReply_WhenTheBotSentNothing_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/nothing");

        // Act
        var act = () => nick.LastReply;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("The bot has sent nothing in the chat with Nick.");
    }

    [Fact]
    public async Task Current_AfterAnEdit_ShouldShowTheEdit()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;

        // Act
        await nick.TapsAsync("A");

        // Assert
        using (new AssertionScope())
        {
            nick.Current(menu)!.ToString().Should().Be("Bot: Nick picked A");
            menu.ToString().Should().StartWith("Bot: Pick one");
        }
    }

    [Fact]
    public async Task Current_AfterADeletion_ShouldBeNull()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;

        // Act
        await nick.TapsAsync("Dismiss");

        // Assert
        nick.Current(menu).Should().BeNull();
    }

    [Fact]
    public async Task Current_OfAMessageFromAnotherChat_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.PrivateChat("Anna");
        var hello = await anna.SendsAsync("hello");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.Current(hello);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("message")
            .WithMessage("The message \"hello\" is from another chat, not the chat with Nick.*");
    }

    [Fact]
    public async Task RevisionsOf_ShouldListEveryStateOldestFirst()
    {
        // Arrange: Next edits the text, then the buttons.
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/steps");
        var card = nick.LastMessage;

        // Act
        await nick.TapsAsync("Next");

        // Assert
        nick.RevisionsOf(card)
            .Select(x => x.ToString())
            .Should()
            .Equal("Bot: Step 1 [Next]", "Bot: Step 2 [Next]", "Bot: Step 2 [Done]");
    }

    [Fact]
    public async Task RevisionsOf_AnUneditedMessage_ShouldHoldOneRevision()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var hello = await nick.SendsAsync("hello");

        // Assert
        nick.RevisionsOf(hello).Should().ContainSingle().Which.ToString().Should().Be("Nick: hello");
    }

    [Fact]
    public async Task RevisionsOf_AUsersEdit_ShouldBeARevision()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var hello = await nick.SendsAsync("hello");

        // Act
        await nick.EditsAsync(hello, "hello there");

        // Assert
        nick.RevisionsOf(hello).Select(x => x.ToString()).Should().Equal("Nick: hello", "Nick: hello there");
    }

    [Fact]
    public async Task RevisionsOf_AfterADeletion_ShouldKeepTheHistory()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;
        await nick.TapsAsync("Small");

        // Act
        await nick.TapsAsync("Remove");

        // Assert
        nick.RevisionsOf(card)
            .Select(x => x.ToString())
            .Should()
            .Equal("Bot: Pick a size [Small] [Large] [Remove]", "Bot: Size 250 [Small] [Large] [Remove]");
    }
}
