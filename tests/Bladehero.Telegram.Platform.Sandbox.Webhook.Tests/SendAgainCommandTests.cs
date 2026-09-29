using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class SendAgainCommandTests
{
    [Fact]
    public async Task Again_ShouldSendTheReplyOnceMoreAndSaySo()
    {
        // Arrange
        await using var bot = await WebhookBot.StartAsync();
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
