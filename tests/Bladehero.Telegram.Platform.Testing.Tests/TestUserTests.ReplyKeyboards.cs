using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task PressesAsync_ShouldSendTheLabel()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/keyboard");

        // Act
        var pressed = await nick.PressesAsync("Tea");

        // Assert
        using (new AssertionScope())
        {
            pressed.ToString().Should().Be("Nick: Tea");
            pressed.Message.ReplyToMessage.Should().BeNull();
            nick.LastMessage.ToString().Should().Be("Bot: Tea");
            nick.ReplyKeyboard!.ToString().Should().Be("[Tea] [Coffee] / [/whoami]");
        }
    }

    [Fact]
    public async Task PressesAsync_InAGroup_ShouldReplyToTheKeyboardsMessage()
    {
        // Arrange: the reply is to the bot, so it reaches the bot whatever the group's privacy.
        await using var bot = await TestBot.StartAsync();
        var anna = bot.GroupChat("Family").Member("Anna");
        await anna.SendsAsync("/keyboard");
        var keyboard = anna.LastMessage;

        // Act
        var pressed = await anna.PressesAsync("Tea");

        // Assert
        using (new AssertionScope())
        {
            pressed.Message.ReplyToMessage!.Id.Should().Be(keyboard.Id);
            anna.LastMessage.ToString().Should().Be("Bot: Tea");
        }
    }

    [Fact]
    public async Task PressesAsync_ACommandLabel_ShouldCarryABotCommand()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/keyboard");

        // Act
        var pressed = await nick.PressesAsync("/whoami");

        // Assert
        using (new AssertionScope())
        {
            pressed.Message.Entities.Should().ContainSingle().Which.Type.Should().Be(MessageEntityType.BotCommand);
            nick.LastMessage.ToString().Should().Be("Bot: You are Nick");
        }
    }

    [Fact]
    public async Task PressesAsync_AOneTimeKeyboard_ShouldHideItButLeaveItUsable()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/onetime");

        // Act
        await nick.PressesAsync("Yes");
        var hidden = nick.ReplyKeyboard!;
        await nick.PressesAsync("No");

        // Assert
        using (new AssertionScope())
        {
            hidden.IsOneTime.Should().BeTrue();
            hidden.IsHidden.Should().BeTrue();
            nick.LastMessage.ToString().Should().Be("Bot: No");
        }
    }

    [Fact]
    public async Task PressesAsync_WithoutAKeyboard_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.PressesAsync("Tea");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Nick has no reply keyboard to press.");
    }

    [Fact]
    public async Task PressesAsync_AMissingLabel_ShouldNameTheButtons()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/keyboard");

        // Act
        var act = () => nick.PressesAsync("Juice");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Nick sees no \"Juice\" on the reply keyboard. The buttons are \"Tea\", \"Coffee\", \"/whoami\"."
            );
    }

    [Fact]
    public async Task PressesAsync_ARequestContactButton_ShouldSayItIsUnsupported()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/contact");

        // Act
        var act = () => nick.PressesAsync("Share");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("The \"Share\" button asks for a contact, which FakeBotApi doesn't support yet.");
    }

    [Fact]
    public async Task ReplyKeyboard_ShouldPersistAcrossPlainAndInlineMessages()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/keyboard");

        // Act
        await nick.SendsAsync("hello");
        await nick.SendsAsync("/menu");

        // Assert
        nick.ReplyKeyboard!.ToString().Should().Be("[Tea] [Coffee] / [/whoami]");
    }

    [Fact]
    public async Task ReplyKeyboard_AfterReplyKeyboardRemove_ShouldBeGone()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/keyboard");

        // Act
        await nick.SendsAsync("/nokeyboard");

        // Assert
        nick.ReplyKeyboard.Should().BeNull();
    }

    [Fact]
    public async Task ReplyKeyboard_WhenSelective_ShouldShowOnlyForTheMentionedUser()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna", username: "anna_k");
        var nick = family.Member("Nick");

        // Act
        await nick.SendsAsync("/selective");

        // Assert
        using (new AssertionScope())
        {
            anna.ReplyKeyboard!.ToString().Should().Be("[A]");
            nick.ReplyKeyboard.Should().BeNull();
        }
    }

    [Fact]
    public async Task SendsAsync_AfterAForceReply_ShouldReplyToItOnce()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/ask");
        var question = nick.LastMessage;

        // Act
        var answer = await nick.SendsAsync("Nick");
        var next = await nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            answer.Message.ReplyToMessage!.Id.Should().Be(question.Id);
            next.Message.ReplyToMessage.Should().BeNull();
            nick.ReplyKeyboard.Should().BeNull();
        }
    }

    [Fact]
    public async Task ReplyKeyboard_WhenSelectiveInAPrivateChat_ShouldShowForTheUser()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act: the keyboard mentions @anna_k, who isn't in this chat.
        await nick.SendsAsync("/selective");

        // Assert
        nick.ReplyKeyboard!.ToString().Should().Be("[A]");
    }

    [Fact]
    public async Task SendsAsync_AfterASelectiveForceReplyInAPrivateChat_ShouldReplyToIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var question = await bot
            .Api.CreateClient()
            .SendMessage(nick.Chat.Id, "Your name?", replyMarkup: new ForceReplyMarkup { Selective = true });

        // Act
        var answer = await nick.SendsAsync("Nick");

        // Assert
        answer.Message.ReplyToMessage!.Id.Should().Be(question.Id);
    }
}
