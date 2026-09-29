using FluentAssertions;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Loyalty;

public sealed class JoinTests
{
    [Fact]
    public async Task Join_ShouldWelcomeANewMember()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/join");

        // Assert
        nick.LastMessage.Text.Should().Be("Welcome to the club, Nick!");
    }

    [Fact]
    public async Task Join_Twice_ShouldSayAlreadyAMember()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/join");

        // Act
        await nick.SendsAsync("/join");

        // Assert
        nick.LastMessage.Text.Should().Be("You're already a member.");
    }
}
