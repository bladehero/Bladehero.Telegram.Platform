using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

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
                .Equal("Nick: /points", "Bot: Nick, you have 30 points. [Redeem 10] [Redeem 50] [✖ Close]");
        }
    }

    [Fact]
    public async Task RedeemButton_ShouldUpdateTheTappedCardInPlace()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        var card = nick.LastMessage;

        // Act
        await nick.TapsAsync("Redeem 10");

        // Assert
        var cards = Cards(nick);
        using (new AssertionScope())
        {
            cards.Should().ContainSingle().Which.Id.Should().Be(card.Id);
            cards[0].IsEdited.Should().BeTrue();
            cards[0].Text.Should().Be("Nick, you have 30 points.");
        }
    }

    [Fact]
    public async Task RedeemButton_WhenTheEditIsRefused_ShouldReplaceTheCard()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        var card = nick.LastMessage;
        bot.Api.Fail("editMessageText", new BotApiError(400, "Bad Request: message can't be edited"), times: 1);
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.TapsAsync("Redeem 10");

        // Assert
        var cards = Cards(nick);
        using (new AssertionScope())
        {
            bot.Api.Calls.Skip(calls)
                .Select(x => x.Method)
                .Should()
                .Equal("answerCallbackQuery", "editMessageText", "sendMessage", "deleteMessage");
            cards.Should().ContainSingle().Which.Id.Should().NotBe(card.Id);
            cards[0].Text.Should().Be("Nick, you have 30 points.");
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
    public async Task RedeemButton_AfterLeaving_ShouldBeAnsweredSilently()
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
            answer.ToString().Should().Be("Answered silently");
            nick.Messages.Single(x => x.Id == card.Id).ToString().Should().Be(card.ToString());
        }
    }

    [Fact]
    public async Task RedeemButton_WithDataFromAnOlderVersion_ShouldSayItIsNoLongerActive()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        var card = nick.LastMessage;

        // Act
        await bot.SendAsync(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "old-tap",
                    From = new User { Id = nick.Id, FirstName = nick.FirstName },
                    Message = card.Message,
                    ChatInstance = nick.Chat.Id.ToString(),
                    Data = "redeem:abc",
                },
            }
        );

        // Assert
        var answer = bot.Api.Calls.Last(x => x.Method == "answerCallbackQuery").Parameters;
        using (new AssertionScope())
        {
            answer["callback_query_id"]!.GetValue<string>().Should().Be("old-tap");
            answer["text"]!.GetValue<string>().Should().Be("That button is no longer active.");
            nick.Messages.Single(x => x.Id == card.Id).ToString().Should().Be(card.ToString());
        }
    }

    [Fact]
    public async Task RedeemButton_PickedByItsData_ShouldRedeemThosePoints()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 60));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");

        // Act
        var answer = await nick.TapsAsync<Redeem>(x => x.Points == 50);

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Redeemed 50 points");
            nick.LastMessage.Text.Should().Be("Nick, you have 10 points.");
        }
    }

    [Fact]
    public async Task RedeemButton_OnAnotherMembersCard_ShouldAlertAndSpendNothing()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40), ("Anna", 30));
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");
        var anna = office.Member("Anna");
        await nick.SendsAsync("/points");
        var before = office.Messages.Select(x => x.ToString()).ToArray();

        // Act
        var answer = await anna.TapsAsync("Redeem 10");

        // Assert
        var members = bot.Services.GetRequiredService<MemberDirectory>();
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Alert: This card is Nick's — send /points for your own.");
            office.Messages.Select(x => x.ToString()).Should().Equal(before);
            members.Find(nick.Id)!.Points.Should().Be(40);
            members.Find(anna.Id)!.Points.Should().Be(30);
        }
    }

    private static TestMessage[] Cards(TestUser member) =>
        [.. member.Messages.Where(x => x.Buttons.Contains("Redeem 10"))];
}
