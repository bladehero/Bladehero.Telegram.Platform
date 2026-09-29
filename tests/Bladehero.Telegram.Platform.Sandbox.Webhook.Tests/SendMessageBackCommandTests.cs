using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class SendMessageBackCommandTests
{
    [Fact]
    public async Task Message_ShouldBeRepliedToThroughTheWebhook()
    {
        // Arrange
        await using var bot = await WebhookBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: Reply: hello [Again]");
    }

    [Fact]
    public async Task Message_InAGroup_ShouldBeRepliedToInTheGroup()
    {
        // Arrange
        await using var bot = await WebhookBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsAsync("hi all");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: Reply: hi all [Again]");
    }

    [Fact]
    public async Task Message_WhenTelegramRefusesTheReply_ShouldFailTheTest()
    {
        // Arrange
        await using var bot = await WebhookBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(403);
    }
}
