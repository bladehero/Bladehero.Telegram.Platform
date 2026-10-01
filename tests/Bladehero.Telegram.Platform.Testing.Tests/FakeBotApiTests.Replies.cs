using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task SendMessage_WithReplyParameters_ShouldEmbedTheTargetWithoutItsOwnReply()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var first = await client.SendMessage(Chat, "first");
        var second = await client.SendMessage(Chat, "second", replyParameters: first.Id);

        // Act
        var third = await client.SendMessage(Chat, "third", replyParameters: second.Id);

        // Assert
        using (new AssertionScope())
        {
            third.ReplyToMessage!.Text.Should().Be("second");
            third.ReplyToMessage.ReplyToMessage.Should().BeNull();
        }
    }

    [Fact]
    public async Task SendMessage_ReplyingToAMissingMessage_ShouldBeRefused()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.SendMessage(Chat, "hello", replyParameters: 999);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: message to be replied not found");
    }

    [Fact]
    public async Task SendMessage_ReplyingToAMissingMessageAllowingNone_ShouldSendWithoutAReply()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendMessage(
            Chat,
            "hello",
            replyParameters: new ReplyParameters { MessageId = 999, AllowSendingWithoutReply = true }
        );

        // Assert
        sent.ReplyToMessage.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_WithTheLegacyReplyToMessageId_ShouldReply()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var first = await client.SendMessage(Chat, "first");

        // Act
        var reply = await client.SendRequest(new LegacyReplyRequest(Chat, first.Id));

        // Assert
        reply.ReplyToMessage!.Id.Should().Be(first.Id);
    }

    [Theory]
    [InlineData("another chat")]
    [InlineData("a quote")]
    public async Task SendMessage_ReplyingToAnotherChatOrQuoting_ShouldBeRefused(string reply)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var first = await client.SendMessage(Chat, "first");
        var parameters =
            reply == "a quote"
                ? new ReplyParameters { MessageId = first.Id, Quote = "fir" }
                : new ReplyParameters { MessageId = first.Id, ChatId = 7 };

        // Act
        var act = () => client.SendMessage(Chat, "hello", replyParameters: parameters);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: FakeBotApi does not support replies to another chat or quotes yet");
    }

    [Fact]
    public async Task ForwardMessage_ShouldCarryTheOriginAndDropCallbackButtons()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var original = await client.SendMessage(
            Chat,
            "Pick one",
            replyMarkup: InlineKeyboardButton.WithCallbackData("A", "pick:A")
        );

        // Act
        var forwarded = await client.ForwardMessage(7, Chat, original.Id);

        // Assert
        using (new AssertionScope())
        {
            forwarded.Chat.Id.Should().Be(7);
            forwarded.From!.IsBot.Should().BeTrue();
            forwarded.Text.Should().Be("Pick one");
            var origin = forwarded.ForwardOrigin.Should().BeOfType<MessageOriginUser>().Subject;
            origin.SenderUser.Id.Should().Be(original.From!.Id);
            origin.Date.Should().Be(original.Date);
            forwarded.ReplyMarkup.Should().BeNull();
        }
    }

    [Fact]
    public async Task ForwardMessage_WithOnlyLinkButtons_ShouldKeepThem()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var original = await client.SendMessage(
            Chat,
            "Read on",
            replyMarkup: InlineKeyboardButton.WithUrl("Docs", "https://example.com")
        );

        // Act
        var forwarded = await client.ForwardMessage(7, Chat, original.Id);

        // Assert
        forwarded.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Should().ContainSingle(x => x.Text == "Docs");
    }

    [Fact]
    public async Task ForwardMessage_OfAMissingMessage_ShouldBeRefused()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.ForwardMessage(7, Chat, 999);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: message to forward not found");
    }

    [Fact]
    public async Task CopyMessage_ShouldReturnOnlyTheNewId()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var original = await client.SendMessage(Chat, "Your limit is near");

        // Act
        var copy = await client.CopyMessage(7, Chat, original.Id);

        // Assert
        var copied = api.MessagesIn(7).Single();
        using (new AssertionScope())
        {
            copy.Id.Should().Be(copied["message_id"]!.GetValue<int>());
            copied["text"]!.GetValue<string>().Should().Be("Your limit is near");
            copied.ContainsKey("forward_origin").Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(null, false, "A cat", false)]
    [InlineData("A renamed cat", false, "A renamed cat", false)]
    [InlineData("", false, null, false)]
    [InlineData(null, true, "A cat", true)]
    public async Task CopyMessage_ShouldKeepOrReplaceTheCaptionAndAttachOnlyAGivenKeyboard(
        string? caption,
        bool keyboard,
        string? copiedCaption,
        bool copiedKeyboard
    )
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var original = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray()), "cat.jpg"),
            caption: "A cat",
            replyMarkup: InlineKeyboardButton.WithCallbackData("Rename", "rename")
        );

        // Act
        await client.CopyMessage(
            7,
            Chat,
            original.Id,
            caption: caption,
            replyMarkup: keyboard
                ? new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Again", "again"))
                : null
        );

        // Assert
        var copied = api.MessagesIn(7).Single();
        using (new AssertionScope())
        {
            copied["caption"]?.GetValue<string>().Should().Be(copiedCaption);
            copied.ContainsKey("caption").Should().Be(copiedCaption is not null);
            copied.ContainsKey("reply_markup").Should().Be(copiedKeyboard);
            copied["photo"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CopyMessage_OfATextMessageWithACaption_ShouldIgnoreTheCaption()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var original = await client.SendMessage(Chat, "Your limit is near");

        // Act
        await client.CopyMessage(7, Chat, original.Id, caption: "A caption");

        // Assert
        var copied = api.MessagesIn(7).Single();
        using (new AssertionScope())
        {
            copied["text"]!.GetValue<string>().Should().Be("Your limit is near");
            copied.ContainsKey("caption").Should().BeFalse();
        }
    }

    [Fact]
    public async Task CopyMessage_OfAMissingMessage_ShouldBeRefused()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.CopyMessage(7, Chat, 999);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: message to copy not found");
    }

    // Telegram.Bot sends replies only as reply_parameters.
    private sealed class LegacyReplyRequest(long chatId, int replyToMessageId) : RequestBase<Message>("sendMessage")
    {
        public override HttpContent ToHttpContent() =>
            JsonContent.Create(
                new JsonObject
                {
                    ["chat_id"] = chatId,
                    ["text"] = "a reply",
                    ["reply_to_message_id"] = replyToMessageId,
                }
            );
    }
}
