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

    [Fact]
    public async Task WaitForMessageAsync_ShouldReturnAMessageSentAfterTheUpdate()
    {
        // Arrange: the bot answers /later from the background, once the update is handled.
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/later");

        // Act
        var later = await nick.Chat.WaitForMessageAsync(x => x.Text == "later");

        // Assert
        later.IsFromBot.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForMessageAsync_ShouldReturnAMatchingMessageAlreadyThere()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var reply = await nick.Chat.WaitForMessageAsync(x => x.IsFromBot);

        // Assert
        reply.ToString().Should().Be("Bot: hello");
    }

    [Fact]
    public async Task WaitForMessageAsync_ShouldSeeAMessageEditedToMatch()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var waiting = nick.WaitForMessageAsync(x => x.Text == "Nick picked A");

        // Act
        await nick.TapsAsync("A");
        var picked = await waiting;

        // Assert
        picked.IsEdited.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForMessageAsync_WhenNothingMatches_ShouldTimeOutShowingTheChat()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var act = () => nick.Chat.WaitForMessageAsync(x => x.Text == "bye", TimeSpan.FromMilliseconds(300));

        // Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage(
                "No message in the chat with Nick matched within 300 ms. The chat now shows:\nNick: hello\nBot: hello"
            );
    }

    [Fact]
    public async Task WaitForMessageAsync_WhenCancelled_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        using var impatient = new CancellationTokenSource();
        var waiting = nick.Chat.WaitForMessageAsync(x => x.Text == "never", token: impatient.Token);

        // Act
        await impatient.CancelAsync();

        // Assert
        await ((Func<Task>)(() => waiting)).Should().ThrowAsync<OperationCanceledException>();
    }
}
