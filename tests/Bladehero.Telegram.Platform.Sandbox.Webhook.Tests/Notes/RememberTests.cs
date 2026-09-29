using FluentAssertions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Notes;

public sealed class RememberTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Remember_ThenRecall_ShouldGiveItBack(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/remember");
        await nick.SendsAsync("buy milk");
        await nick.SendsAsync("/recall");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Nick: /remember",
                "Bot: What should I remember?",
                "Nick: buy milk",
                "Bot: Got it.",
                "Nick: /recall",
                "Bot: You asked me to remember: buy milk"
            );
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Recall_WithNothingRemembered_ShouldSaySo(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/recall");

        // Assert
        nick.LastMessage.Text.Should().Be("Nothing yet — try /remember");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Remember_ThenACommand_ShouldLetTheCommandThrough(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/remember");

        // Act
        await nick.SendsAsync("/recall");

        // Assert
        nick.LastMessage.Text.Should().Be("Nothing yet — try /remember");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Remember_ByTwoGroupMembers_ShouldKeepTheirNotesApart(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var family = bot.GroupChat("Family");
        var nick = family.Member("Nick");
        var anna = family.Member("Anna");

        // Act
        await nick.SendsAsync("/remember");
        await anna.SendsAsync("/remember");
        await nick.SendsAsync("buy milk");
        await anna.SendsAsync("call mom");
        await nick.SendsAsync("/recall");
        await anna.SendsAsync("/recall");

        // Assert
        family
            .Messages.Where(x => x.IsFromBot)
            .Select(x => x.Text)
            .TakeLast(2)
            .Should()
            .Equal("You asked me to remember: buy milk", "You asked me to remember: call mom");
    }
}
