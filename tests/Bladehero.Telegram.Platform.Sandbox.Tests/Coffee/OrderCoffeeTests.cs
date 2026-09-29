using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Coffee;

public sealed class OrderCoffeeTests
{
    [Fact]
    public async Task Coffee_ShouldAskForTheSize()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee");

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: What size? [Small] [Medium] [Large] [Cancel]");
    }

    [Fact]
    public async Task Coffee_ShouldPlaceTheOrderOnceConfirmed()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee");
        await nick.TapsAsync("Medium");
        await nick.SendsAsync("Nicky");
        await nick.TapsAsync("Confirm");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Nick: /coffee",
                "Bot: Size: Medium ✓",
                "Bot: Whose name goes on the cup?",
                "Nick: Nicky",
                "Bot: Order placed ☕ — a Medium coffee for Nicky."
            );
    }

    [Fact]
    public async Task CancelButton_ShouldEndTheOrder()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");

        // Act
        await nick.TapsAsync("Cancel");
        await nick.SendsAsync("Nicky");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal("Nick: /coffee", "Bot: Order cancelled.", "Nick: Nicky", "Bot: Send /coffee to order one ☕");
    }

    [Fact]
    public async Task CancelCommand_ShouldEndTheOrderInProgress()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        await nick.TapsAsync("Large");

        // Act
        await nick.SendsAsync("/cancel");

        // Assert
        nick.LastMessage.Text.Should().Be("Cancelled your coffee order.");
    }

    [Fact]
    public async Task SizeButton_AfterTheOrderWasCancelled_ShouldOnlySayItIsNoLongerActive()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        await nick.SendsAsync("/cancel");
        var before = nick.Messages.Select(x => x.ToString()).ToArray();

        // Act
        var answer = await nick.TapsAsync("Small");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: That button is no longer active.");
            nick.Messages.Select(x => x.ToString()).Should().Equal(before);
        }
    }

    [Fact]
    public async Task Coffee_InAGroup_ShouldKeepEachMembersOrderApart()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var office = bot.GroupChat("Office");
        var anna = office.Member("Anna");
        var nick = office.Member("Nick");
        await anna.SendsAsync("/coffee");
        var annasCard = office.LastMessage;
        await nick.SendsAsync("/coffee");
        await anna.TapsAsync("Small", on: annasCard);

        // Act
        await nick.SendsAsync("Nick"); // Nick is still picking a size, so this is not a name for his cup
        await anna.SendsAsync("Anna");

        // Assert
        office
            .Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Anna: /coffee",
                "Bot: Size: Small ✓",
                "Nick: /coffee",
                "Bot: What size? [Small] [Medium] [Large] [Cancel]",
                "Bot: Whose name goes on the cup?",
                "Nick: Nick",
                "Bot: Use the buttons above — or /cancel.",
                "Anna: Anna",
                "Bot: A Small coffee for Anna. Place the order? [Confirm] [Cancel]"
            );
    }

    private static Task<TelegramTestHost> StartBotAsync() => SandboxBot.StartAsync();
}
