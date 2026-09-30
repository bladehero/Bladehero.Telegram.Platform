using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    private const string BlockedTheBot =
        "Nick blocked the bot, so the app shows Unblock instead of the message field; call UnblocksBotAsync first.";

    [Fact]
    public async Task BlocksBotAsync_ShouldSendMyChatMemberAsKicked()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.BlocksBotAsync();

        // Assert
        var change = bot
            .Services.GetRequiredService<TestBot.Seen<ChatMemberUpdated>>()
            .All.Should()
            .ContainSingle()
            .Subject;
        using (new AssertionScope())
        {
            change.Chat.Id.Should().Be(nick.Chat.Id);
            change.From.Id.Should().Be(nick.Id);
            change.OldChatMember.Should().BeOfType<ChatMemberMember>().Which.User.IsBot.Should().BeTrue();
            change.NewChatMember.Should().BeOfType<ChatMemberBanned>().Which.User.Username.Should().Be("test_bot");
            nick.HasBlockedBot.Should().BeTrue();
        }
    }

    [Fact]
    public async Task BlocksBotAsync_WhenTheBotDoesNotAskForMyChatMember_ShouldStillBlock()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(receiver: receiver =>
            receiver.AllowedUpdates = [UpdateType.Message]
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.BlocksBotAsync();
        var send = () => bot.Api.CreateClient().SendMessage(nick.Chat.Id, "Your limit is near");

        // Assert
        using (new AssertionScope())
        {
            (await send.Should().ThrowAsync<ApiRequestException>()).Which.ErrorCode.Should().Be(403);
            bot.Services.GetRequiredService<TestBot.Seen<ChatMemberUpdated>>().All.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task BlocksBotAsync_Twice_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.BlocksBotAsync();

        // Act
        var act = () => nick.BlocksBotAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Nick has blocked the bot already.");
    }

    [Fact]
    public async Task UnblocksBotAsync_WhenNotBlocked_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.UnblocksBotAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Nick hasn't blocked the bot.");
    }

    [Fact]
    public async Task BlocksBotAsync_InAGroup_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.GroupChat("Family").Member("Nick");

        // Act
        var act = () => nick.BlocksBotAsync();

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Only a private chat can block the bot; a group member can't.");
    }

    [Fact]
    public async Task SendsAsync_WhileBlocked_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.BlocksBotAsync();

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(BlockedTheBot);
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UnblocksBotAsync_WithRestart_ShouldSendMyChatMemberThenStart()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.BlocksBotAsync();

        // Act
        await nick.UnblocksBotAsync(restart: true);

        // Assert
        using (new AssertionScope())
        {
            bot.Services.GetRequiredService<TestBot.Seen<ChatMemberUpdated>>()
                .All.Select(x => x.NewChatMember.Status)
                .Should()
                .Equal(ChatMemberStatus.Kicked, ChatMemberStatus.Member);
            nick.HasBlockedBot.Should().BeFalse();
            nick.LastMessage.ToString().Should().Be("Nick: /start");
        }
    }
}
