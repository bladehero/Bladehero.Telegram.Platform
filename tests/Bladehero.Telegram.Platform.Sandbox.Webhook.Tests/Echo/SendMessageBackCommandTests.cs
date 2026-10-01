using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Echo;

public sealed class SendMessageBackCommandTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Message_ShouldBeRepliedTo(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: Reply: hello [Again] [Louder]");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Message_InAGroup_ShouldBeRepliedToInTheGroup(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var family = bot.GroupChat("Family");
        await family.MakesBotAdminAsync(); // so it hears plain text

        // Act
        await family.Member("Anna").SendsAsync("hi all");

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: Reply: hi all [Again] [Louder]");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Command_ShouldNotBeEchoed(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/nothing");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /nothing");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Message_WhenTelegramRefusesTheReply_ShouldFailTheTest(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
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
