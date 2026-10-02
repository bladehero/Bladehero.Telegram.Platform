using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.History.InMemory;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// The bot's own ITelegramMessages against the fake, driven as a background job would.
public sealed class TelegramMessagesTests
{
    private static readonly InlineKeyboardMarkup Keyboard = new InlineKeyboardMarkup().AddButton("Redeem 10", "r:10");

    [Fact]
    public async Task ShowAsync_OnTheCard_ShouldEditItInPlace()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);

        // Act
        var shown = await messages.ShowAsync(card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(card);
            nick.Messages.Select(x => x.ToString()).Should().Equal("Bot: You have 40 points. [Redeem 10]");
            nick.RevisionsOf(nick.LastMessage).Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task ShowAsync_WithTheSameTextAndKeyboard_ShouldChangeNothing()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);

        // Act
        var shown = await messages.ShowAsync(card, "You have 50 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(card);
            nick.Messages.Should().ContainSingle().Which.IsEdited.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ShowAsync_OnACardTheBotDeleted_ShouldSendAFreshOne()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);
        await bot.Services.GetRequiredService<ITelegramBotClient>().DeleteMessage(card.ChatId, card.MessageId);

        // Act
        var shown = await messages.ShowAsync(card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().NotBe(card);
            nick.Messages.Should().ContainSingle().Which.Ref.Should().Be(shown);
            nick.LastMessage.ToString().Should().Be("Bot: You have 40 points. [Redeem 10]");
        }
    }

    [Fact]
    public async Task ShowAsync_OnAPhoto_ShouldSendTheTextFreshAndRemoveThePhoto()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var photo = await bot
            .Services.GetRequiredService<ITelegramBotClient>()
            .SendPhoto(nick.Chat.Id, InputFile.FromStream(new MemoryStream([1, 2, 3]), "latte.png"));

        // Act
        var shown = await messages.ShowAsync(TelegramMessageRef.From(photo), "Your latte is on its way.");

        // Assert
        using (new AssertionScope())
        {
            shown.Should().NotBe(TelegramMessageRef.From(photo));
            nick.Messages.Select(x => x.ToString()).Should().Equal("Bot: Your latte is on its way.");
        }
    }

    [Fact]
    public async Task ShowAsync_OnAUsersMessage_ShouldSendAFreshOne()
    {
        // Arrange: in a group where the bot isn't an admin, so it can't delete Anna's message either.
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        var messages = MessagesOf(bot);
        var said = await anna.SendsAsync("/probe");

        // Act
        var shown = await messages.ShowAsync(said.Ref, "Noted.");

        // Assert
        using (new AssertionScope())
        {
            family.LastMessage.Ref.Should().Be(shown);
            family.Messages.Select(x => x.ToString()).Should().Equal("Anna: /probe", "Bot: Noted.");
        }
    }

    [Fact]
    public async Task ReplaceAsync_ShouldSendAtTheBottomThenDeleteTheOld()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);
        await nick.SendsAsync("/probe");
        var mark = bot.Api.Calls.Count;

        // Act
        var replaced = await messages.ReplaceAsync(card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            bot.Api.CallsSince(mark).Select(x => x.Method).Should().Equal("sendMessage", "deleteMessage");
            nick.Messages.Select(x => x.ToString())
                .Should()
                .Equal("Nick: /probe", "Bot: You have 40 points. [Redeem 10]");
            nick.LastMessage.Ref.Should().Be(replaced);
        }
    }

    [Fact]
    public async Task ReplaceAsync_WhenTheOldCantBeDeleted_ShouldClearItsKeyboard()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);
        bot.Api.Fail("deleteMessage", BotApiError.MessageCantBeDeleted, times: 1);

        // Act
        await messages.ReplaceAsync(card, "You have 40 points.", Keyboard);

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal("Bot: You have 50 points.", "Bot: You have 40 points. [Redeem 10]");
    }

    [Fact]
    public async Task DeleteAsync_Twice_ShouldSucceedBothTimes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);

        // Act
        bool[] deleted = [await messages.DeleteAsync(card), await messages.DeleteAsync(card)];

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().Equal(true, true);
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenTelegramWontDelete_ShouldClearTheKeyboard()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);
        bot.Api.Fail("deleteMessage", BotApiError.MessageCantBeDeleted, times: 1);

        // Act
        var deleted = await messages.DeleteAsync(card);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeFalse();
            nick.Messages.Select(x => x.ToString()).Should().Equal("Bot: You have 50 points.");
        }
    }

    [Fact]
    public async Task DeleteAsync_OfAUsersMessageInAGroupWithoutRights_ShouldReturnFalse()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");
        var anna = family.Member("Anna");
        var said = await anna.SendsAsync("/probe");

        // Act
        var deleted = await MessagesOf(bot).DeleteAsync(said.Ref);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeFalse();
            family.Messages.Select(x => x.ToString()).Should().Equal("Anna: /probe");
        }
    }

    [Fact]
    public async Task ClearKeyboardAsync_Twice_ShouldSucceedBothTimes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);

        // Act
        await messages.ClearKeyboardAsync(card);
        var again = () => messages.ClearKeyboardAsync(card);

        // Assert
        using (new AssertionScope())
        {
            await again.Should().NotThrowAsync();
            nick.Messages.Select(x => x.ToString()).Should().Equal("Bot: You have 50 points.");
        }
    }

    [Fact]
    public async Task ClearKeyboardAsync_OnADeletedCard_ShouldSucceed()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var card = await messages.SendAsync(nick.Chat.Id, "You have 50 points.", Keyboard);
        await messages.DeleteAsync(card);

        // Act
        var act = () => messages.ClearKeyboardAsync(card);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TestMessage_Ref_ShouldNameTheMessage()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var said = await nick.SendsAsync("/probe");

        // Assert
        said.Ref.Should().Be(new TelegramMessageRef(nick.Chat.Id, said.Id));
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_WithInMemoryHistory_ShouldFindTheCardAfterAnEdit()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: x => x.AddTelegramHistory().UseInMemory());
        var nick = bot.PrivateChat("Nick");
        var messages = MessagesOf(bot);
        var sizes = new InlineKeyboardMarkup().AddButton(ButtonData.Button("250 ml", new TestBot.Size(250, 1)));
        var card = await messages.SendAsync(nick.Chat.Id, "Which size?", sizes);
        card = await messages.ShowAsync(card, "Which size? Small ones are out.", sizes);

        // Act
        var found = await bot.History.FindLatestWithButtonAsync<TestBot.Size>(nick.Chat.Id);

        // Assert
        found.Should().Be(card);
    }

    private static ITelegramMessages MessagesOf(TelegramTestHost bot) =>
        bot.Services.GetRequiredService<ITelegramMessages>();
}
