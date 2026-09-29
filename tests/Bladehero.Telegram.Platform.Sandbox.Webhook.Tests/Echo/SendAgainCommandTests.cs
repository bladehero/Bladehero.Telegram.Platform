using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Echo;

public sealed class SendAgainCommandTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Again_ShouldSendTheReplyOnceMoreAndSaySo(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var answer = await nick.TapsAsync("Again");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Sent again");
            nick.LastMessage.ToString().Should().Be("Bot: Reply: hello");
        }
    }
}
