using FluentAssertions;
using FluentAssertions.Execution;

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
                "Bot: What should I remember? [Cancel]",
                "Nick: buy milk",
                "Bot: Got it.",
                "Nick: /recall",
                "Bot: You asked me to remember: buy milk"
            );
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Cancel_ShouldForgetTheNote(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/remember");
        var prompt = nick.LastMessage;

        // Act
        var answer = await nick.TapsAsync("Cancel");
        await nick.SendsAsync("buy milk");
        await nick.SendsAsync("/recall");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Cancelled.");
            nick.Messages.Single(x => x.Id == prompt.Id).ToString().Should().Be("Bot: Nothing remembered.");
            nick.LastMessage.Text.Should().Be("Nothing yet — try /remember");
        }
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Cancel_AfterTheNoteWasSaved_ShouldSayItIsNoLongerActive(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/remember");
        var prompt = nick.LastMessage;
        await nick.SendsAsync("buy milk");

        // Act
        var answer = await nick.TapsAsync("Cancel", on: prompt);
        await nick.SendsAsync("/recall");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: That button is no longer active.");
            nick.Messages.Single(x => x.Id == prompt.Id).ToString().Should().Be(prompt.ToString());
            nick.LastMessage.Text.Should().Be("You asked me to remember: buy milk");
        }
    }

    // In webhook mode the two taps are two requests at once, which the conversation's lock takes one at a time.
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Cancel_TappedTwiceAtOnce_ShouldCancelOnce(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/remember");
        var prompt = nick.LastMessage;

        // Act
        var answers = await Task.WhenAll(nick.TapsAsync("Cancel", on: prompt), nick.TapsAsync("Cancel", on: prompt));

        // Assert
        using (new AssertionScope())
        {
            answers
                .Select(x => x.ToString())
                .Should()
                .BeEquivalentTo("Notification: Cancelled.", "Notification: That button is no longer active.");
            nick.Messages.Single(x => x.Id == prompt.Id).Text.Should().Be("Nothing remembered.");
        }
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
        await family.MakesBotAdminAsync(); // so it hears the notes
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
