using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types;

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

    [Fact]
    public async Task Coffee_Twice_ShouldRetireTheFirstCard()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        var first = nick.LastMessage;

        // Act
        await nick.SendsAsync("/coffee");

        // Assert
        using (new AssertionScope())
        {
            nick.Messages.Single(x => x.Id == first.Id).Buttons.Should().BeEmpty();
            nick.LastMessage.ToString().Should().Be("Bot: What size? [Small] [Medium] [Large] [Cancel]");
        }
    }

    [Fact]
    public async Task SizeButton_OnAnEarlierOrdersCard_ShouldSayItIsNoLongerActive()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        var earlier = nick.LastMessage;
        await nick.SendsAsync("/cancel");
        await nick.SendsAsync("/coffee");
        var current = nick.LastMessage;

        // Act: the earlier tap must not pick a size for the order in progress.
        var answer = await nick.TapsAsync("Small", on: earlier);
        await nick.TapsAsync("Medium", on: current);

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: That button is no longer active.");
            nick.Messages.Single(x => x.Id == current.Id).Text.Should().Be("Size: Medium ✓");
        }
    }

    [Fact]
    public async Task SizeButton_FromAViewThatMissedThePick_ShouldSayItIsNoLongerActive()
    {
        // Arrange: Nick's app still shows the size card after he picked Medium.
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        var card = nick.LastMessage;
        await nick.TapsAsync("Medium");
        var large = card.Message.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Single(x => x.Text == "Large");

        // Act
        await bot.SendAsync(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "stale-tap",
                    From = new User { Id = nick.Id, FirstName = "Nick" },
                    Message = card.Message,
                    ChatInstance = "1",
                    Data = large.CallbackData,
                },
            }
        );

        // Assert
        using (new AssertionScope())
        {
            bot.Api.Calls.Last(x => x.Method == "answerCallbackQuery").Parameters["text"]!
                .GetValue<string>()
                .Should()
                .Be("That button is no longer active.");
            nick.Messages.Single(x => x.Id == card.Id).Text.Should().Be("Size: Medium ✓");
        }
    }

    [Fact]
    public async Task SizeButton_OnAnotherMembersCard_ShouldSayItIsNotTheirs()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");
        var anna = office.Member("Anna");
        await nick.SendsAsync("/coffee");
        var nicksCard = office.LastMessage;
        await anna.SendsAsync("/coffee");
        var annasCard = office.LastMessage;

        // Act: Anna's own order must still be at her size step afterwards.
        var answer = await anna.TapsAsync("Small", on: nicksCard);
        await anna.TapsAsync("Large", on: annasCard);

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: This order isn't yours.");
            office.Messages.Single(x => x.Id == nicksCard.Id).ToString().Should().Be(nicksCard.ToString());
            office.Messages.Single(x => x.Id == annasCard.Id).Text.Should().Be("Size: Large ✓");
        }
    }

    [Theory]
    [InlineData("size", "button")]
    [InlineData("size", "command")]
    [InlineData("name", "button")]
    [InlineData("name", "command")]
    [InlineData("confirm", "button")]
    [InlineData("confirm", "command")]
    public async Task Cancel_AtEveryStep_ShouldEndTheOrder(string step, string how)
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");

        if (step is "name" or "confirm")
        {
            await nick.TapsAsync("Medium");
        }

        if (step is "confirm")
        {
            await nick.SendsAsync("Nicky");
        }

        // Act
        if (how is "button")
        {
            await nick.TapsAsync("Cancel");
        }
        else
        {
            await nick.SendsAsync("/cancel");
        }

        await nick.SendsAsync("Nicky");

        // Assert
        nick.LastMessage.Text.Should().Be("Send /coffee to order one ☕");
    }

    [Fact]
    public async Task Coffee_WithASizeArgument_ShouldSkipTheSizeStep()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee large");
        await nick.SendsAsync("Nicky");

        // Assert
        nick.Messages.Where(x => x.IsFromBot)
            .Select(x => x.ToString())
            .Should()
            .Equal(
                "Bot: Size: Large ✓",
                "Bot: Whose name goes on the cup?",
                "Bot: A Large coffee for Nicky. Place the order? [Confirm] [Cancel]"
            );
    }

    [Fact]
    public async Task Coffee_WithTheSizeOnTheNextLine_ShouldSkipTheSizeStep()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee\nsmall");

        // Assert
        nick.Messages.Where(x => x.IsFromBot)
            .Select(x => x.ToString())
            .Should()
            .Equal("Bot: Size: Small ✓", "Bot: Whose name goes on the cup? [Cancel]");
    }

    [Fact]
    public async Task Coffee_WithAnUnknownSize_ShouldAskForIt()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee huge");

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: What size? [Small] [Medium] [Large] [Cancel]");
    }

    [Fact]
    public async Task Coffee_AddressedToTheBotInAGroup_ShouldWork()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var office = bot.GroupChat("Office");

        // Act
        await office.Member("Nick").SendsAsync("/coffee@test_bot");

        // Assert
        office.LastMessage.ToString().Should().Be("Bot: What size? [Small] [Medium] [Large] [Cancel]");
    }

    [Fact]
    public async Task Coffee_ForACyrillicAndEmojiName_ShouldKeepIt()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");
        await nick.TapsAsync("Medium");

        // Act
        await nick.SendsAsync("Ника ☕");
        await nick.TapsAsync("Confirm");

        // Assert
        nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Ника ☕.");
    }

    [Fact]
    public async Task Coffee_TwoCustomersAtOnce_ShouldKeepTheirOrdersApart()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        var anna = bot.PrivateChat("Anna");

        // Act
        await Task.WhenAll(nick.SendsAsync("/coffee"), anna.SendsAsync("/coffee"));
        await Task.WhenAll(nick.TapsAsync("Medium"), anna.TapsAsync("Large"));
        await Task.WhenAll(nick.SendsAsync("Nicky"), anna.SendsAsync("Annie"));
        await Task.WhenAll(nick.TapsAsync("Confirm"), anna.TapsAsync("Confirm"));

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Nicky.");
            anna.LastMessage.Text.Should().Be("Order placed ☕ — a Large coffee for Annie.");
        }
    }

    [Fact]
    public async Task Coffee_TextDuringTheSizeStep_ShouldHintAndKeepTheStep()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");

        // Act
        await nick.SendsAsync("hello");
        await nick.TapsAsync("Medium");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Nick: /coffee",
                "Bot: Size: Medium ✓",
                "Nick: hello",
                "Bot: Use the buttons above — or /cancel.",
                "Bot: Whose name goes on the cup? [Cancel]"
            );
    }

    [Fact]
    public async Task SizeButton_ShouldCarryItsConversation()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/coffee");

        // Assert
        nick.LastMessage.Message.ReplyMarkup!.InlineKeyboard.First()
            .Select(button => button.CallbackData)
            .Should()
            .HaveCount(3)
            .And.AllSatisfy(data =>
                data.Should().MatchRegex($@"^coffee-size:(small|medium|large)@{nick.Id}\.[a-z0-9]{{8}}$")
            );
    }

    [Fact]
    public async Task CancelButton_FromAnotherMembersOrder_ShouldSayItIsNotTheirs()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");
        var anna = office.Member("Anna");
        await nick.SendsAsync("/coffee large");
        var prompt = office.LastMessage;

        // Act
        var answer = await anna.TapsAsync("Cancel", on: prompt);
        await nick.SendsAsync("Nicky");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: This order isn't yours.");
            office
                .LastMessage.ToString()
                .Should()
                .Be("Bot: A Large coffee for Nicky. Place the order? [Confirm] [Cancel]");
        }
    }

    private static Task<TelegramTestHost> StartBotAsync() => SandboxBot.StartAsync();
}
