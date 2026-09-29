using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Loyalty;

public sealed class RedeemTests
{
    [Fact]
    public async Task Redeem_WithAnAmount_ShouldSpendIt()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/redeem 10");

        // Assert
        nick.LastMessage.Text.Should().Be("Redeemed 10 points — enjoy a free cookie! 30 left.");
    }

    [Fact]
    public async Task Redeem_WithTheAmountOnTheNextLine_ShouldSpendIt()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/redeem\n10");

        // Assert
        nick.LastMessage.Text.Should().Be("Redeemed 10 points — enjoy a free cookie! 30 left.");
    }

    [Fact]
    public async Task Redeem_WithoutAnAmount_ShouldAskHowMany()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/redeem");

        // Assert
        nick.LastMessage.Text.Should().Be("How many? Try /redeem 10");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    public async Task Redeem_WithANonNumber_ShouldSaySo(string amount)
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync($"/redeem {amount}");

        // Assert
        nick.LastMessage.Text.Should().Be("That's not a number of points.");
    }

    [Fact]
    public async Task Redeem_MoreThanTheBalance_ShouldSaySo()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 5));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/redeem 10");

        // Assert
        nick.LastMessage.Text.Should().Be("You have only 5 points.");
    }

    [Fact]
    public async Task RedeemButton_WithEnoughPoints_ShouldNotifyAndUpdateTheCard()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");

        // Act
        var answer = await nick.TapsAsync("Redeem 10");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Redeemed 10 points");
            nick.Messages.Select(x => x.ToString())
                .Should()
                .Equal("Nick: /points", "Bot: Nick, you have 30 points. [Redeem 10] [Redeem 50]");
        }
    }

    [Fact]
    public async Task RedeemButton_WithoutEnoughPoints_ShouldAlert()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        var card = nick.LastMessage.ToString();

        // Act
        var answer = await nick.TapsAsync("Redeem 50");

        // Assert
        using (new AssertionScope())
        {
            answer.IsAlert.Should().BeTrue();
            answer.Text.Should().Be("You have only 40 points.");
            nick.LastMessage.ToString().Should().Be(card);
        }
    }

    [Fact]
    public async Task RedeemButton_AfterLeaving_ShouldGoUnanswered()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        var card = nick.LastMessage;
        await nick.SendsAsync("/leave");

        // Act
        var answer = await nick.TapsAsync("Redeem 10", on: card);

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("No answer");
            nick.Messages.Single(x => x.Id == card.Id).ToString().Should().Be(card.ToString());
        }
    }
}
