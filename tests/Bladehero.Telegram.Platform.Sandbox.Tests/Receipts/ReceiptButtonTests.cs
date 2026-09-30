using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;

public sealed class ReceiptButtonTests
{
    private const string Card = "Bot: Receipt: 12.40 EUR → 12 points. [Add 12 points] [Discard]";

    private static byte[] Receipt => "a receipt from Luigi's"u8.ToArray();

    [Fact]
    public async Task AddButton_ShouldEarnThePointsAndCloseTheCard()
    {
        // Arrange
        await using var bot = await StartAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsPhotoAsync(Receipt);
        var card = nick.LastMessage;

        // Act
        var answer = await nick.TapsAsync("Add 12 points");

        // Assert
        var closed = nick.Messages.Single(x => x.Id == card.Id);
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: Added 12 points");
            closed.Text.Should().Be("Receipt: 12.40 EUR → added 12 points ✓");
            closed.IsEdited.Should().BeTrue();
            closed.Buttons.Should().BeEmpty();
            PointsOf(bot, nick).Should().Be(52);
        }
    }

    [Fact]
    public async Task DiscardButton_ShouldRemoveOnlyTheButtons()
    {
        // Arrange
        await using var bot = await StartAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsPhotoAsync(Receipt);

        // Act
        var answer = await nick.TapsAsync("Discard");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Answered silently");
            nick.LastMessage.ToString().Should().Be("Bot: Receipt: 12.40 EUR → 12 points.");
            bot.Api.Calls.Select(x => x.Method).Should().Contain("editMessageReplyMarkup");
            PointsOf(bot, nick).Should().Be(40);
        }
    }

    [Fact]
    public async Task AddButton_TappedTwiceAtOnce_ShouldAddOnce()
    {
        // Arrange
        await using var bot = await StartAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsPhotoAsync(Receipt);
        var card = nick.LastMessage;

        // Act
        var answers = await Task.WhenAll(
            nick.TapsAsync("Add 12 points", on: card),
            nick.TapsAsync("Add 12 points", on: card)
        );

        // Assert
        using (new AssertionScope())
        {
            answers
                .Select(x => x.Text)
                .Should()
                .BeEquivalentTo(["Added 12 points", "This receipt is no longer pending."]);
            PointsOf(bot, nick).Should().Be(52);
        }
    }

    [Fact]
    public async Task AddButton_AfterARestart_ShouldSayNoLongerPendingAndStripTheCard()
    {
        // Arrange
        var api = new FakeBotApi();
        var members = SandboxBot.Members(api, ("Nick", 40));
        var reader = SandboxBot.Using<IReceiptReader>(new ScriptedReceiptReader());
        await using (var before = await SandboxBot.StartAsync(api, members, reader))
        {
            await before.PrivateChat("Nick").SendsPhotoAsync(Receipt);
        }

        await using var bot = await SandboxBot.StartAsync(api, members, reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        var answer = await nick.TapsAsync("Add 12 points");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: This receipt is no longer pending.");
            nick.LastMessage.ToString().Should().Be("Bot: Receipt: 12.40 EUR → 12 points.");
            PointsOf(bot, nick).Should().Be(40);
        }
    }

    [Fact]
    public async Task ReceiptButton_OnAnotherMembersCard_ShouldAlert()
    {
        // Arrange
        await using var bot = await StartAsync(("Nick", 40), ("Anna", 30));
        var office = bot.GroupChat("Office");
        var nick = office.Member("Nick");
        var anna = office.Member("Anna");
        await nick.SendsPhotoAsync(Receipt);

        // Act
        var answer = await anna.TapsAsync("Add 12 points");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Alert: This receipt isn't yours.");
            office.LastMessage.ToString().Should().Be(Card);
            PointsOf(bot, anna).Should().Be(30);
        }
    }

    [Fact]
    public async Task ReceiptCard_ShouldWriteItsButtonDataAsBefore()
    {
        // Arrange
        await using var bot = await StartAsync(("Nick", 40));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync(Receipt);

        // Assert
        nick.LastMessage.Message.ReplyMarkup!.InlineKeyboard.SelectMany(row => row)
            .Select(button => button.CallbackData)
            .Should()
            .SatisfyRespectively(
                add => add.Should().MatchRegex($"^receipt:{nick.Id}:[0-9a-f]{{12}}:add$"),
                discard => discard.Should().MatchRegex($"^receipt:{nick.Id}:[0-9a-f]{{12}}:discard$")
            );
    }

    private static Task<TelegramTestHost> StartAsync(params (string FirstName, int Points)[] members) =>
        SandboxBot.StartWithMembersAsync(SandboxBot.Using<IReceiptReader>(new ScriptedReceiptReader()), members);

    private static int PointsOf(TelegramTestHost bot, TestUser member) =>
        bot.Services.GetRequiredService<MemberDirectory>().Find(member.Id)!.Points;
}
