using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TestUserTests
{
    [Fact]
    public async Task ReactsAsync_WhenTheBotAsksForReactions_ShouldDeliverMessageReaction()
    {
        // Arrange
        await using var bot = await StartAskingForReactionsAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");
        var reply = nick.LastMessage;

        // Act
        await nick.ReactsAsync(reply, "👍");

        // Assert
        var reaction = bot
            .Services.GetRequiredService<TestBot.Seen<MessageReactionUpdated>>()
            .All.Should()
            .ContainSingle()
            .Subject;
        using (new AssertionScope())
        {
            reaction.MessageId.Should().Be(reply.Id);
            reaction.User!.Id.Should().Be(nick.Id);
            reaction.OldReaction.Should().BeEmpty();
            reaction
                .NewReaction.Should()
                .ContainSingle()
                .Which.Should()
                .BeOfType<ReactionTypeEmoji>()
                .Which.Emoji.Should()
                .Be("👍");
            nick.Messages.Single(x => x.Id == reply.Id).Reactions.Should().Equal("👍");
            reply.Reactions.Should().BeEmpty("a snapshot keeps the reactions it was taken with");
        }
    }

    [Fact]
    public async Task ReactsAsync_WithNull_ShouldTakeTheReactionBack()
    {
        // Arrange
        await using var bot = await StartAskingForReactionsAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");
        await nick.ReactsAsync(nick.LastMessage, "👍");

        // Act
        await nick.ReactsAsync(nick.LastMessage, null);

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Reactions.Should().BeEmpty();
            bot.Services.GetRequiredService<TestBot.Seen<MessageReactionUpdated>>()
                .All[^1]
                .OldReaction.Should()
                .ContainSingle();
        }
    }

    [Fact]
    public async Task ReactsAsync_WhenTheBotDoesNotAsk_ShouldThrow()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var act = () => nick.ReactsAsync(nick.LastMessage, "👍");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Telegram sends message_reaction only to a bot that asks for it: add UpdateType.MessageReaction to "
                    + "AllowedUpdates."
            );
    }

    [Fact]
    public async Task ReactsAsync_InAGroupWhereTheBotIsNotAdmin_ShouldApplyWithoutAnUpdate()
    {
        // Arrange
        await using var bot = await StartAskingForReactionsAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        await anna.SendsAsync("/whoami");

        // Act
        await anna.ReactsAsync(family.LastMessage, "❤");

        // Assert
        using (new AssertionScope())
        {
            family.LastMessage.Reactions.Should().Equal("❤");
            bot.Services.GetRequiredService<TestBot.Seen<MessageReactionUpdated>>().All.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("❤️")]
    [InlineData("🙂")]
    public async Task ReactsAsync_WithAnEmojiTelegramDoesNotOffer_ShouldThrow(string emoji)
    {
        // Arrange
        await using var bot = await StartAskingForReactionsAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var act = () => nick.ReactsAsync(nick.LastMessage, emoji);

        // Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .WithParameterName("emoji")
            .Which.Message.Should()
            .StartWith($"\"{emoji}\" isn't a reaction Telegram offers.");
    }

    private static Task<TelegramTestHost> StartAskingForReactionsAsync() =>
        TestBot.StartAsync(receiver: receiver =>
            receiver.AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery, UpdateType.MessageReaction]
        );
}
