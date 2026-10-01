using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Loyalty;

public sealed class PointsTests
{
    private const string NicksCard = "Bot: Nick, you have 40 points. [Redeem 10] [Redeem 50] [✖ Close]";

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
    public async Task Points_Again_ShouldReplaceTheCardAtTheBottom()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        var card = await ShowCardAsync(nick);
        await nick.SendsAsync("Hello");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            nick.Messages.Should().NotContain(x => x.Id == card.Id);
            Cards(nick).Should().ContainSingle().Which.Id.Should().Be(nick.LastMessage.Id);
            nick.LastMessage.ToString().Should().Be(NicksCard);
        }
    }

    [Fact]
    public async Task Points_AfterEarningPoints_ShouldShowTheNewBalanceAtTheBottom()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        await OrderACoffeeAsync(nick);

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            Cards(nick).Should().ContainSingle().Which.Id.Should().Be(nick.LastMessage.Id);
            nick.LastMessage.Text.Should().Be("Nick, you have 50 points.");
        }
    }

    [Fact]
    public async Task Points_WhenTheOldCardIsAlreadyGone_ShouldStillSendANewOne()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        bot.Api.Fail("deleteMessage", new BotApiError(400, "Bad Request: message to delete not found"), times: 1);
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.SendsAsync("/points");

        // Assert
        using (new AssertionScope())
        {
            bot.Api.Calls.Skip(calls).Select(x => x.Method).Should().Equal("deleteMessage", "sendMessage");
            nick.LastMessage.ToString().Should().Be(NicksCard);
        }
    }

    [Fact]
    public async Task Points_WhenTheOldCardCannotBeDeleted_ShouldStillSendANewOne()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        bot.Api.Fail("deleteMessage", new BotApiError(400, "Bad Request: message can't be deleted"), times: 1);

        // Act
        await nick.SendsAsync("/points");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points", NicksCard, "Nick: /points", NicksCard);
    }

    [Fact]
    public async Task Points_AfterClosingTheCard_ShouldSendANewOneWithoutDeleting()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        await nick.TapsAsync("✖ Close");
        var calls = bot.Api.Calls.Count;

        // Act
        await nick.SendsAsync("/points");

        // Assert
        bot.Api.Calls.Skip(calls).Select(x => x.Method).Should().Equal("sendMessage");
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
    public async Task CloseButton_ShouldRemoveTheCard()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");

        // Act
        var answer = await nick.TapsAsync("✖ Close");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Answered silently");
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points");
        }
    }

    [Fact]
    public async Task CloseButton_OnAnotherMembersCard_ShouldAlertAndKeepIt()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40), ("Anna", 30));
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");
        var anna = office.Member("Anna");
        await nick.SendsAsync("/points");
        var before = office.Messages.Select(x => x.ToString()).ToArray();

        // Act
        var answer = await anna.TapsAsync("✖ Close");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Alert: This card is Nick's — send /points for your own.");
            office.Messages.Select(x => x.ToString()).Should().Equal(before);
        }
    }

    [Fact]
    public async Task CloseButton_WhenTheCardCannotBeDeleted_ShouldRemoveItsButtons()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/points");
        bot.Api.Fail("deleteMessage", new BotApiError(400, "Bad Request: message can't be deleted"), times: 1);

        // Act
        await nick.TapsAsync("✖ Close");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /points", "Bot: Nick, you have 40 points.");
    }

    [Fact]
    public async Task PointsCard_ShouldWriteItsButtonDataAsBefore()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        DataOf(nick.LastMessage)
            .Should()
            .Equal($"redeem:{nick.Id}:10", $"redeem:{nick.Id}:50", $"points-close:{nick.Id}");
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

    private static string?[] DataOf(TestMessage message) =>
        [.. message.Message.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Select(button => button.CallbackData)];

    private static TestMessage[] Cards(TestUser member) =>
        [.. member.Messages.Where(x => x.Buttons.Contains("Redeem 10"))];
}
