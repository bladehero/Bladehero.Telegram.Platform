using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Echo;

public sealed class LouderButtonTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Louder_ShouldRaiseTheReplyStepByStep(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var firstAnswer = await nick.TapsAsync("Louder");
        var once = nick.LastMessage;
        var secondAnswer = await nick.TapsAsync("Louder");
        var twice = nick.LastMessage;

        // Assert
        using (new AssertionScope())
        {
            firstAnswer.ToString().Should().Be("Answered silently");
            once.ToString().Should().Be("Bot: REPLY: HELLO! [Again] [Louder]");
            secondAnswer.ToString().Should().Be("Answered silently");
            twice.ToString().Should().Be("Bot: REPLY: HELLO!! [Again] [Louder]");
            twice.Id.Should().Be(once.Id);
        }
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Louder_AtTheTop_ShouldLeaveOnlyAgain(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");
        await nick.TapsAsync("Louder");
        await nick.TapsAsync("Louder");

        // Act
        await nick.TapsAsync("Louder");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: REPLY: HELLO!!! [Again]");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Echo_ShouldWriteTheLouderDataAsBefore(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.LastMessage.Message.ReplyMarkup!.InlineKeyboard.SelectMany(row => row)
            .Select(button => button.CallbackData)
            .Should()
            .Equal("again", "louder:1");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Again_AfterLouder_ShouldSendTheLoudText(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");
        await nick.TapsAsync("Louder");

        // Act
        await nick.TapsAsync("Again");

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: REPLY: HELLO!");
    }
}
