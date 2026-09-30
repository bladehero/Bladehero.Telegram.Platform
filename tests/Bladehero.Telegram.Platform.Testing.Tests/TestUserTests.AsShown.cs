using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task TapsAsync_AsShown_ShouldSendTheSnapshotsDataWithTheMessageAsItNowStands()
    {
        // Arrange: the card as first sent, then redrawn by a tap on Small.
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;
        await nick.TapsAsync("Small");

        // Act
        var answer = await nick.TapsAsync("Large", on: card, asShown: true);

        // Assert
        var seen = bot.Services.GetRequiredService<TestBot.SeenTaps>().Last;
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Size 400");
            seen.Data.Should().Be(DataOf(card, "Large"));
            seen.Message!.Text.Should().Be("Size 250");
        }
    }

    [Fact]
    public async Task TapsAsync_AsShownOnADeletedMessage_ShouldSendAnInaccessibleMessage()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;
        await nick.TapsAsync("Remove");

        // Act
        var answer = await nick.TapsAsync("Large", on: card, asShown: true);

        // Assert
        var seen = bot.Services.GetRequiredService<TestBot.SeenTaps>().Last;
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: That card is gone");
            seen.Message!.Date.Should().Be(default, "Telegram.Bot reads date 0 as no date");
            seen.Message.Id.Should().Be(card.Id);
            seen.Message.Chat.Id.Should().Be(nick.Chat.Id);
            seen.Message.Text.Should().BeNull();
        }
    }

    [Fact]
    public async Task TapsAsync_AsShownFalse_ShouldTapTheMessageAsItNowStands()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;
        await nick.TapsAsync("Small");
        var redrawn = nick.Current(card)!;

        // Act
        await nick.TapsAsync("Large", on: card, asShown: false);

        // Assert
        bot.Services.GetRequiredService<TestBot.SeenTaps>()
            .Last.Data.Should()
            .Be(DataOf(redrawn, "Large"))
            .And.NotBe(DataOf(card, "Large"));
    }

    [Fact]
    public async Task TapsAsync_AsShownWithAButtonTheSnapshotLacks_ShouldNameItsButtons()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;

        // Act
        var act = () => nick.TapsAsync("Medium", on: card, asShown: true);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be(
                "Nick sees no \"Medium\" button on \"Pick a size\" as Nick's app showed it. The buttons are \"Small\", "
                    + "\"Large\", \"Remove\"."
            );
    }

    [Fact]
    public async Task TapsAsync_AsShownWithAnAmbiguousPredicate_ShouldSaySoAsTheAppShowedIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;

        // Act
        var act = () => nick.TapsAsync<TestBot.Size>(null, on: card, asShown: true);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be(
                "\"Pick a size\", as Nick's app showed it, shows more than one Size button, so which one Nick taps is "
                    + "ambiguous: Size { Ml = 250, Version = 1 }, Size { Ml = 400, Version = 1 }. Pick one with "
                    + "TapsAsync<Size>(b => …)."
            );
    }

    [Fact]
    public async Task TapsAsync_AsShownFromAnotherChat_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.PrivateChat("Anna");
        await anna.SendsAsync("/size");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.TapsAsync("Large", on: anna.LastMessage, asShown: true);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("\"Pick a size\" is not in the chat with Nick.");
    }

    [Fact]
    public async Task TapsAsync_ByButtonTypeAsShown_ShouldUseTheSnapshotsData()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/size");
        var card = nick.LastMessage;
        await nick.TapsAsync("Small");

        // Act
        var answer = await nick.TapsAsync<TestBot.Size>(x => x.Ml == 400, on: card, asShown: true);

        // Assert
        var seen = bot.Services.GetRequiredService<TestBot.SeenTaps>().Last;
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Size 400");
            seen.Data.Should().Be(DataOf(card, "Large"));
            seen.Message!.Text.Should().Be("Size 250");
        }
    }

    private static string? DataOf(TestMessage message, string button) =>
        message.Message.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Single(x => x.Text == button).CallbackData;
}
