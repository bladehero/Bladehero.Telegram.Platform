using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Coffee;

public sealed class EditCupNameTests
{
    [Fact]
    public async Task EditingTheCupName_AtTheConfirmStep_ShouldUpdateTheCardAndTheOrder()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee medium");
        var name = await nick.SendsAsync("Nicky");

        // Act
        await nick.EditsAsync(name, "Nicolas");
        var card = nick.LastMessage;
        await nick.TapsAsync("Confirm");

        // Assert
        using (new AssertionScope())
        {
            card.ToString().Should().Be("Bot: A Medium coffee for Nicolas. Place the order? [Confirm] [Cancel]");
            nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Nicolas.");
        }
    }

    [Fact]
    public async Task EditingAnotherMessage_ShouldChangeNothing()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        var aside = await nick.SendsAsync("One moment");
        await nick.TapsAsync("Medium");
        await nick.SendsAsync("Nicky");
        var card = nick.LastMessage;

        // Act
        await nick.EditsAsync(aside, "Nicolas");
        var cardAfterTheEdit = nick.Messages.Single(x => x.Id == card.Id);
        await nick.TapsAsync("Confirm");

        // Assert
        using (new AssertionScope())
        {
            cardAfterTheEdit.ToString().Should().Be(card.ToString());
            cardAfterTheEdit.IsEdited.Should().BeFalse();
            nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Nicky.");
        }
    }

    [Fact]
    public async Task EditingTheCupName_AfterTheOrderIsPlaced_ShouldChangeNothing()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee medium");
        var name = await nick.SendsAsync("Nicky");
        await nick.TapsAsync("Confirm");
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.EditsAsync(name, "Nicolas");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Nicky.");
            bot.Api.Calls.Should().HaveCount(calls);
        }
    }
}
