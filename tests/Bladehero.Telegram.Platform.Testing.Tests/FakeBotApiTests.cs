using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class FakeBotApiTests
{
    private const long Chat = 42;

    private static readonly InlineKeyboardMarkup YesNo = new([
        [InlineKeyboardButton.WithCallbackData("Yes", "yes"), InlineKeyboardButton.WithCallbackData("No", "no")],
    ]);

    [Fact]
    public async Task SendMessage_ShouldAnswerWithTheMessageAsTelegramWould()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: YesNo);

        // Assert
        using (new AssertionScope())
        {
            sent.Chat.Id.Should().Be(Chat);
            sent.Text.Should().Be("Continue?");
            sent.From!.IsBot.Should().BeTrue();
            sent.ReplyMarkup!.InlineKeyboard.SelectMany(row => row)
                .Select(x => x.CallbackData)
                .Should()
                .Equal("yes", "no");
        }
    }

    [Fact]
    public async Task SendMessage_WithAReplyKeyboard_ShouldNotAttachItToTheMessage()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: new ReplyKeyboardMarkup("Yes"));

        // Assert
        sent.ReplyMarkup.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_ShouldNumberMessagesPerChat()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var first = await client.SendMessage(Chat, "one");
        var second = await client.SendMessage(Chat, "two");
        var elsewhere = await client.SendMessage(7, "one");

        // Assert
        new[] { first.Id, second.Id, elsewhere.Id }
            .Should()
            .Equal(1, 2, 1);
    }

    [Fact]
    public async Task Calls_ShouldRecordEveryRequestInOrderWithItsParameters()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();

        // Act
        await client.SendMessage(Chat, "Continue?");
        await client.AnswerCallbackQuery("query", "Done");

        // Assert
        using (new AssertionScope())
        {
            api.Calls.Select(x => x.Method).Should().Equal("sendMessage", "answerCallbackQuery");
            api.Calls[0].Parameters["text"]!.GetValue<string>().Should().Be("Continue?");
            api.Calls[1].Parameters["text"]!.GetValue<string>().Should().Be("Done");
        }
    }

    [Fact]
    public async Task EditMessageText_ShouldReplaceTheTextAndKeyboard()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?");

        // Act
        var edited = await client.EditMessageText(Chat, sent.Id, "Sure?", replyMarkup: YesNo);

        // Assert
        using (new AssertionScope())
        {
            edited.Id.Should().Be(sent.Id);
            edited.Text.Should().Be("Sure?");
            edited.ReplyMarkup.Should().NotBeNull();
            edited.EditDate.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task EditMessageText_WithoutAKeyboard_ShouldRemoveTheOneShown()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: YesNo);

        // Act
        var edited = await client.EditMessageText(Chat, sent.Id, "Done.");

        // Assert
        edited.ReplyMarkup.Should().BeNull();
    }

    [Fact]
    public async Task EditMessageText_WhenNothingChanges_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: YesNo);

        // Act
        var act = () => client.EditMessageText(Chat, sent.Id, "Continue?", replyMarkup: YesNo);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message is not modified");
    }

    [Fact]
    public async Task EditMessageText_OnAMessageTheBotDidNotSend_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();
        var nick = api.Person("Nick");
        var chat = api.PrivateChatWith(nick);
        var received = api.Receive(chat, nick, "hello");

        // Act
        var act = () => client.EditMessageText(chat, received["message_id"]!.GetValue<int>(), "bye");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message can't be edited");
    }

    [Fact]
    public async Task EditMessageReplyMarkup_WithoutAKeyboard_ShouldKeepTheTextAndRemoveTheButtons()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: YesNo);

        // Act
        var edited = await client.EditMessageReplyMarkup(Chat, sent.Id, replyMarkup: null);

        // Assert
        using (new AssertionScope())
        {
            edited.Text.Should().Be("Continue?");
            edited.ReplyMarkup.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteMessage_ShouldRemoveTheMessageSoItCanNoLongerBeEdited()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?");
        await client.DeleteMessage(Chat, sent.Id);

        // Act
        var act = () => client.EditMessageText(Chat, sent.Id, "Sure?");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message to edit not found");
    }

    [Fact]
    public async Task DeleteMessage_WhenTheMessageDoesNotExist_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var act = () => client.DeleteMessage(Chat, 99);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message to delete not found");
    }

    [Fact]
    public async Task GetMe_ShouldAnswerWithTheBot()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var me = await client.GetMe();

        // Assert
        using (new AssertionScope())
        {
            me.Id.Should().Be(client.BotId);
            me.IsBot.Should().BeTrue();
        }
    }

    [Fact]
    public async Task AMethodTheFakeDoesNotAnswer_ShouldFailNamingTheMethod()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

        // Act
        var act = () => client.SendDice(Chat);

        // Assert
        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        using (new AssertionScope())
        {
            failure.Which.ErrorCode.Should().Be(404);
            failure.Which.Message.Should().Contain("sendDice");
        }
    }
}
