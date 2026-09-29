using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Welcome;

public sealed class StartTests
{
    [Fact]
    public async Task Start_ShouldGreetThenShowTheMenuThenATip()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/start");

        // Assert: unmarked commands run first, then priority (1, 0), then (1).
        var replies = nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text!).ToArray();
        using (new AssertionScope())
        {
            replies.Should().HaveCount(3);
            replies[0].Should().Be("Welcome to the coffee shop, Nick!");
            replies[1].Should().StartWith("Here is what I can do:");
            replies[2].Should().Be("Tip: members earn 10 points a coffee — /join");
        }
    }

    [Fact]
    public async Task Help_ShouldShowOnlyTheMenu()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/help");

        // Assert
        nick.Messages.Where(x => x.IsFromBot)
            .Should()
            .ContainSingle()
            .Which.Text.Should()
            .StartWith("Here is what I can do:");
    }

    [Fact]
    public async Task Help_ShouldListWhatTheCommandMenuPublished()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/help");

        // Assert
        nick.LastMessage.Text!.Split('\n')
            .Skip(1)
            .Should()
            .Equal(bot.Api.CommandMenu().Select(x => $"/{x.Command} - {x.Description}"));
    }
}
