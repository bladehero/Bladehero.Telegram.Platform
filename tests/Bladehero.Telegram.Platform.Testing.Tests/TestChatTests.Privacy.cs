using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestChatTests
{
    [Fact]
    public async Task SendsAsync_PlainTextInAGroup_ShouldNotReachAPrivacyModeBot()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            family.Messages.Select(x => x.ToString()).Should().Equal("Anna: hello");
            bot.Api.Calls.Should().NotContain(x => x.Method == "sendMessage");
        }
    }

    [Theory]
    [InlineData("/whoami")]
    [InlineData("/whoami@test_bot")]
    [InlineData("/whoami@TEST_BOT")]
    public async Task SendsAsync_ACommandForThisBotInAGroup_ShouldReachIt(string command)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync(command);

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: You are Anna");
    }

    [Fact]
    public async Task SendsAsync_ACommandForAnotherBotInAGroup_ShouldNotReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("/whoami@other_bot");

        // Assert
        family.Messages.Select(x => x.ToString()).Should().Equal("Anna: /whoami@other_bot");
    }

    [Fact]
    public async Task RepliesAsync_ToTheBotInAGroup_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        await family.Member("Anna").SendsAsync("/menu");
        var menu = family.LastMessage;

        // Act
        await family.Member("Nick").RepliesAsync(menu, "hello");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: hello");
    }

    [Fact]
    public async Task RepliesAsync_ToAnotherUserInAGroup_ShouldNotReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var question = await family.Member("Anna").SendsAsync("/whoami");

        // Act
        await family.Member("Nick").RepliesAsync(question, "hello");

        // Assert
        family.LastMessage.ToString().Should().Be("Nick (↩ Anna: /whoami): hello");
    }

    [Fact]
    public async Task SendsAsync_MentioningTheBotInAGroup_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("hi @Test_Bot");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: hi @Test_Bot");
    }

    [Fact]
    public async Task SendsAsync_MentioningAnotherBotWhoseNameStartsTheSame_ShouldNotReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("hi @test_bot_two");

        // Assert
        family.Messages.Select(x => x.ToString()).Should().Equal("Anna: hi @test_bot_two");
    }

    [Fact]
    public async Task EditsAsync_OfAMessageTheBotNeverGot_ShouldNotReachItEither()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        var hello = await anna.SendsAsync("hello");

        // Act
        await anna.EditsAsync(hello, "hello @test_bot");

        // Assert
        family.Messages.Select(x => x.ToString()).Should().Equal("Anna: hello @test_bot");
    }

    [Fact]
    public async Task TapsAsync_InAGroup_ShouldAlwaysReachTheBot()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.GroupChat("Family").Member("Anna");
        await anna.SendsAsync("/menu");

        // Act
        var answer = await anna.TapsAsync("A");

        // Assert
        answer.ToString().Should().Be("Notification: You picked A");
    }

    [Fact]
    public async Task SendsAsync_PlainTextInAGroup_WhenTheBotIsAdmin_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        await family.MakesBotAdminAsync();

        // Act
        await family.Member("Anna").SendsAsync("hello");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: hello");
    }

    [Fact]
    public async Task SendsAsync_PlainTextInAGroup_WithPrivacyOff_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.Api.PrivacyMode = false;
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("hello");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: hello");
    }

    [Fact]
    public async Task MakesBotAdminAsync_ShouldSendMyChatMemberAsAdministrator()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.MakesBotAdminAsync();

        // Assert
        var change = bot.Services.GetRequiredService<TestBot.SeenMemberships>().All.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            change.Chat.Id.Should().Be(family.Id);
            change.OldChatMember.Should().BeOfType<ChatMemberMember>();
            var admin = change.NewChatMember.Should().BeOfType<ChatMemberAdministrator>().Subject;
            admin.CanDeleteMessages.Should().BeTrue();
            admin.User.Username.Should().Be("test_bot");
            family.BotIsAdmin.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MakesBotAdminAsync_InAPrivateChat_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.Chat.MakesBotAdminAsync();

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Only a group has admins; this is the chat with Nick.");
    }

    [Fact]
    public async Task DemotesBotAsync_ShouldMakeItAMemberAgain()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        await family.MakesBotAdminAsync(canDeleteMessages: false);

        // Act
        await family.DemotesBotAsync();
        await family.Member("Anna").SendsAsync("hello");

        // Assert
        var change = bot.Services.GetRequiredService<TestBot.SeenMemberships>().All[^1];
        using (new AssertionScope())
        {
            change
                .OldChatMember.Should()
                .BeOfType<ChatMemberAdministrator>()
                .Which.CanDeleteMessages.Should()
                .BeFalse();
            change.NewChatMember.Should().BeOfType<ChatMemberMember>();
            family.BotIsAdmin.Should().BeFalse();
            family.Messages.Select(x => x.ToString()).Should().Equal("Anna: hello");
        }
    }
}
