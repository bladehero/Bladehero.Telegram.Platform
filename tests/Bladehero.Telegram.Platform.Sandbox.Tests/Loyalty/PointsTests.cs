using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Loyalty;

public sealed class PointsTests
{
    private const string NicksCard = "Bot: Nick, you have 40 points. [Redeem 10] [Redeem 50]";

    [Fact]
    public async Task Points_FromAStranger_ShouldBeIgnoredWithoutAnyCall()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points");
            bot.Api.Calls.Should().HaveCount(calls);
        }
    }

    [Fact]
    public async Task Points_FromAMemberSeededBeforeStart_ShouldShowTheirPoints()
    {
        // Arrange
        var api = new FakeBotApi();
        await using var bot = await SandboxBot.StartAsync(api, SandboxBot.Members((api.UserIdOf("Nick"), "Nick", 40)));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        nick.LastMessage.ToString().Should().Be(NicksCard);
    }

    [Fact]
    public async Task Points_Twice_ShouldKeepOneCard()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points", NicksCard, "Nick: /points");
    }

    [Fact]
    public async Task Points_AfterEarningPoints_ShouldEditTheCardInPlace()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        var card = await ShowCardAsync(nick);
        await OrderACoffeeAsync(nick);

        // Act
        await nick.SendsAsync("/points");

        // Assert
        var cards = Cards(nick);
        using (new AssertionScope())
        {
            cards.Should().ContainSingle().Which.Id.Should().Be(card.Id);
            cards[0].Text.Should().Be("Nick, you have 50 points.");
            cards[0].IsEdited.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Points_WhenTheCardIsGone_ShouldSendANewOne()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        bot.Api.Fail(
            "editMessageText",
            new BotApiError(400, "Bad Request: message to edit not found"),
            times: 1,
            chatId: nick.Chat.Id
        );
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            bot.Api.Calls.Skip(calls).Select(x => x.Method).Should().Equal("editMessageText", "sendMessage");
            nick.LastMessage.ToString().Should().Be(NicksCard);
        }
    }

    [Fact]
    public async Task Points_WhenTheEditIsRefused_ShouldReplaceTheCard()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        var card = await ShowCardAsync(nick);
        bot.Api.Fail("editMessageText", new BotApiError(400, "Bad Request: message can't be edited"), times: 1);
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            bot.Api.Calls.Skip(calls)
                .Select(x => x.Method)
                .Should()
                .Equal("editMessageText", "deleteMessage", "sendMessage");
            Cards(nick).Should().ContainSingle().Which.Id.Should().NotBe(card.Id);
        }
    }

    [Fact]
    public async Task Points_WhenTheOldCardCannotBeDeletedEither_ShouldStillSendANewOne()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        bot.Api.Fail("editMessageText", new BotApiError(400, "Bad Request: message can't be edited"), times: 1);
        bot.Api.Fail("deleteMessage", new BotApiError(400, "Bad Request: message can't be deleted"), times: 1);

        // Act
        await nick.SendsAsync("/points");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points", NicksCard, "Nick: /points", NicksCard);
    }

    [Fact]
    public async Task Points_ByAMemberInAGroup_ShouldBeAnsweredInTheGroup()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        office.LastMessage.ToString().Should().Be(NicksCard);
    }

    [Fact]
    public async Task Points_ByAStrangerInTheSameGroup_ShouldBeIgnored()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var office = bot.GroupChat("Office");
        var anna = office.Member("Anna");

        // Act
        await anna.SendsAsync("/points");

        // Assert
        office.Messages.Select(x => x.ToString()).Should().Equal("Anna: /points");
    }

    [Fact]
    public async Task Coffee_ConfirmedByAMember_ShouldEarnTenPoints()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await OrderACoffeeAsync(nick);
        await nick.SendsAsync("/points");

        // Assert
        nick.LastMessage.Text.Should().Be("Nick, you have 50 points.");
    }

    private static async Task<TestMessage> ShowCardAsync(TestUser member)
    {
        await member.SendsAsync("/points");
        return member.LastMessage;
    }

    private static async Task OrderACoffeeAsync(TestUser customer)
    {
        await customer.SendsAsync("/coffee");
        await customer.TapsAsync("Medium");
        await customer.SendsAsync(customer.FirstName);
        await customer.TapsAsync("Confirm");
    }

    private static TestMessage[] Cards(TestUser member) =>
        [.. member.Messages.Where(x => x.Buttons.Contains("Redeem 10"))];
}
