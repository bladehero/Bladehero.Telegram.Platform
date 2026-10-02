using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed partial class TelegramHistoryEntriesTests
{
    private const long Group = -1001234567890;
    private const long Channel = -1009876543210;
    private const long Nick = 7000000001;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromCall_SendMessage_ShouldRecordTheMessageAsShown()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ChatId = Group,
            Text = "<b>Hi</b>",
            ParseMode = ParseMode.Html,
        };

        // Act
        var entries = Call(request, MessageIn(Group, 10, text: "Hi"));

        // Assert
        using (new AssertionScope())
        {
            entries
                .Should()
                .ContainSingle()
                .Which.Should()
                .BeEquivalentTo(
                    new
                    {
                        Time = Now,
                        Direction = TelegramHistoryDirection.Outgoing,
                        Kind = "sendMessage",
                        UpdateId = (int?)null,
                        ChatId = Group,
                        MessageId = 10,
                        Text = "Hi",
                        ErrorCode = (int?)null,
                        Error = (string?)null,
                    }
                );
            Json(entries[0])["request"]!["text"]!.GetValue<string>().Should().Be("<b>Hi</b>");
            Json(entries[0])["result"]!["message_id"]!.GetValue<int>().Should().Be(10);
        }
    }

    [Fact]
    public void FromCall_SendMessageToAUsername_ShouldTakeTheChatFromTheResult()
    {
        // Arrange
        var request = new SendMessageRequest { ChatId = "@news", Text = "Hi" };

        // Act
        var entries = Call(request, MessageIn(Channel, 11, text: "Hi"));

        // Assert
        entries.Should().ContainSingle().Which.ChatId.Should().Be(Channel);
    }

    [Fact]
    public void FromCall_RefusedCall_ShouldRecordTheCodeAndTheDescription()
    {
        // Arrange
        var request = new BanChatMemberRequest { ChatId = "@news", UserId = Nick };

        // Act
        var entries = Call(request, error: new ApiRequestException("Bad Request: chat not found", 400));

        // Assert
        using (new AssertionScope())
        {
            entries
                .Should()
                .ContainSingle()
                .Which.Should()
                .BeEquivalentTo(
                    new
                    {
                        Kind = "banChatMember",
                        ChatId = (long?)null,
                        UserId = Nick,
                        ErrorCode = 400,
                        Error = "Bad Request: chat not found",
                    }
                );
            Json(entries[0])["result"].Should().BeNull();
        }
    }

    [Fact]
    public void FromCall_SendMediaGroup_ShouldRecordEachMessage()
    {
        // Arrange
        var request = new SendMediaGroupRequest
        {
            ChatId = Group,
            Media =
            [
                new InputMediaPhoto(InputFile.FromString("AgAD1")),
                new InputMediaPhoto(InputFile.FromString("AgAD2")),
            ],
        };
        Message[] album = [PhotoIn(Group, 13, "AgAD1-big"), PhotoIn(Group, 14, "AgAD2-big")];

        // Act
        var entries = Call(request, album);

        // Assert
        using (new AssertionScope())
        {
            entries.Select(x => x.MessageId).Should().Equal(13, 14);
            entries.Select(x => x.FileId).Should().Equal("AgAD1-big", "AgAD2-big");
            entries.Select(x => Json(x)["result"]!["message_id"]!.GetValue<int>()).Should().Equal(13, 14);
        }
    }

    [Fact]
    public void FromCall_CopyMessage_ShouldTakeTheChatFromTheRequest()
    {
        // Arrange
        var request = new CopyMessageRequest
        {
            ChatId = Group,
            FromChatId = Nick,
            MessageId = 5,
        };

        // Act
        var entries = Call(request, new MessageId { Id = 15 });

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Group, MessageId = 15 });
    }

    [Fact]
    public void FromCall_ForwardMessages_ShouldRecordEachForwardedMessage()
    {
        // Arrange
        var request = new ForwardMessagesRequest
        {
            ChatId = Group,
            FromChatId = Nick,
            MessageIds = [5, 6],
        };

        // Act
        var entries = Call(
            request,
            new MessageId[]
            {
                new() { Id = 16 },
                new() { Id = 17 },
            }
        );

        // Assert
        using (new AssertionScope())
        {
            entries.Select(x => x.MessageId).Should().Equal(16, 17);
            entries.Should().OnlyContain(x => x.ChatId == Group);
        }
    }

    [Fact]
    public void FromCall_ForwardThatFailed_ShouldNotTakeTheSourceChatsIds()
    {
        // Arrange
        var request = new ForwardMessagesRequest
        {
            ChatId = Group,
            FromChatId = Nick,
            MessageIds = [5, 6],
        };

        // Act
        var entries = Call(request, error: new ApiRequestException("Bad Request: message to forward not found", 400));

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Group, MessageId = (int?)null });
    }

    [Fact]
    public void FromCall_DeleteMessages_ShouldRecordEachMessage()
    {
        // Arrange
        var request = new DeleteMessagesRequest { ChatId = Group, MessageIds = [10, 11] };

        // Act
        var entries = Call(request, true);

        // Assert
        entries.Select(x => x.MessageId).Should().Equal(10, 11);
    }

    [Fact]
    public void FromCall_InlineEdit_ShouldRecordTheInlineMessage()
    {
        // Arrange
        var request = new EditInlineMessageTextRequest { InlineMessageId = "AAAAinline", Text = "Done" };

        // Act
        var entries = Call(request, true);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "editMessageText",
                    InlineMessageId = "AAAAinline",
                    Text = "Done",
                    ChatId = (long?)null,
                }
            );
    }

    [Fact]
    public void FromCall_EditMessageText_ShouldRecordTheTextAsShown()
    {
        // Arrange
        var request = new EditMessageTextRequest
        {
            ChatId = Group,
            MessageId = 10,
            Text = "<i>Large</i>",
            ParseMode = ParseMode.Html,
        };

        // Act
        var entries = Call(request, MessageIn(Group, 10, text: "Large"));

        // Assert
        entries.Should().ContainSingle().Which.Text.Should().Be("Large");
    }

    [Fact]
    public void FromCall_AnswerCallbackQuery_ShouldRecordTheAnswersText()
    {
        // Arrange
        var request = new AnswerCallbackQueryRequest { CallbackQueryId = "4382bfdwdsb323b2d9", Text = "Saved" };

        // Act
        var entries = Call(request, true);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Text = "Saved",
                    ChatId = (long?)null,
                    UserId = (long?)null,
                }
            );
    }

    [Fact]
    public void FromCall_GetFile_ShouldRecordTheFileId()
    {
        // Arrange
        var request = new GetFileRequest { FileId = "BQAD" };
        var file = new TGFile
        {
            FileId = "BQAD",
            FileUniqueId = "AgADBQAD",
            FilePath = "documents/file_1.csv",
        };

        // Act
        var entries = Call(request, file);

        // Assert
        entries.Should().ContainSingle().Which.FileId.Should().Be("BQAD");
    }

    [Fact]
    public void FromCall_SendPhotoFromAStream_ShouldRecordTheLargestSizeAndNoBytes()
    {
        // Arrange
        var upload = new MemoryStream("PNGBYTES"u8.ToArray());
        var request = new SendPhotoRequest { ChatId = Group, Photo = InputFile.FromStream(upload, "photo.png") };
        upload.Dispose();

        // Act
        var entries = Call(request, PhotoIn(Group, 12, "AgAD-big"));

        // Assert
        using (new AssertionScope())
        {
            entries.Should().ContainSingle().Which.FileId.Should().Be("AgAD-big");
            Json(entries[0])["request"]!["photo"]!.GetValue<string>().Should().StartWith("attach://");
            entries[0].Json.Should().NotContain("PNGBYTES").And.NotContain(Convert.ToBase64String("PNGBYTES"u8));
        }
    }

    [Fact]
    public void FromCall_SendDocument_ShouldRecordTheFileIdAndName()
    {
        // Arrange
        var request = new SendDocumentRequest
        {
            ChatId = Group,
            Document = InputFile.FromStream(new MemoryStream([1, 2, 3]), "report.csv"),
        };
        var sent = MessageIn(Group, 13);
        sent.Document = new Document
        {
            FileId = "BQAD",
            FileUniqueId = "AgADBQAD",
            FileName = "report.csv",
        };

        // Act
        var entries = Call(request, sent);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { FileId = "BQAD", FileName = "report.csv" });
    }

    [Fact]
    public void FromCall_SetWebhook_ShouldLeaveTheSecretTokenOut()
    {
        // Arrange
        var request = new SetWebhookRequest
        {
            Url = "https://bot.example.com/telegram/updates",
            SecretToken = "s3cr3t-t0ken",
        };

        // Act
        var entries = Call(request, true);

        // Assert
        using (new AssertionScope())
        {
            Json(entries.Single())["request"]!["url"]!
                .GetValue<string>()
                .Should()
                .Be("https://bot.example.com/telegram/updates");
            entries.Single().Json.Should().NotContain("secret_token").And.NotContain("s3cr3t-t0ken");
        }
    }

    [Fact]
    public void FromCall_SendInvoice_ShouldLeaveTheProviderTokenOut()
    {
        // Arrange
        var request = new SendInvoiceRequest
        {
            ChatId = Nick,
            Title = "Coffee",
            Description = "A large latte",
            Payload = "order-1",
            Currency = "USD",
            Prices = [new LabeledPrice("Latte", 450)],
            ProviderToken = "284685063:TEST:abc",
        };

        // Act
        var entries = Call(request, MessageIn(Nick, 20));

        // Assert
        entries.Single().Json.Should().NotContain("provider_token").And.NotContain("284685063:TEST:abc");
    }

    [Fact]
    public void FromCall_ThatTimedOut_ShouldRecordTheMessageWithoutACode()
    {
        // Arrange
        var request = new SendMessageRequest { ChatId = Group, Text = "Hi" };

        // Act
        var entries = Call(request, error: new RequestException("Request timed out"));

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { ErrorCode = (int?)null, Error = "Request timed out" });
    }

    [Fact]
    public void FromCall_WithoutKeepJson_ShouldLeaveTheJsonOut()
    {
        // Arrange
        var request = new SendMessageRequest { ChatId = Group, Text = "Hi" };

        // Act
        var entries = TelegramHistoryEntries.FromCall(request, MessageIn(Group, 10), null, null, Now, keepJson: false);

        // Assert
        entries.Should().ContainSingle().Which.Json.Should().BeNull();
    }

    [Fact]
    public void FromCall_WithinAnUpdate_ShouldCarryItsId()
    {
        // Arrange
        var request = new SendMessageRequest { ChatId = Group, Text = "Hi" };

        // Act
        var entries = TelegramHistoryEntries.FromCall(
            request,
            MessageIn(Group, 10),
            null,
            new Update { Id = 7 },
            Now,
            keepJson: true
        );

        // Assert
        entries.Should().ContainSingle().Which.UpdateId.Should().Be(7);
    }

    private static IReadOnlyList<TelegramHistoryEntry> Call(
        IRequest request,
        object? result = null,
        Exception? error = null
    ) => TelegramHistoryEntries.FromCall(request, result, error, null, Now, keepJson: true);

    private static JsonNode Json(TelegramHistoryEntry entry) => JsonNode.Parse(entry.Json!)!;

    private static Message MessageIn(long chatId, int id, string? text = null) =>
        new()
        {
            Id = id,
            Date = Now.UtcDateTime,
            Chat = new Chat { Id = chatId, Type = chatId < 0 ? ChatType.Supergroup : ChatType.Private },
            Text = text,
        };

    private static Message PhotoIn(long chatId, int id, string largest)
    {
        var message = MessageIn(chatId, id);
        message.Photo =
        [
            new PhotoSize
            {
                FileId = largest + "-small",
                FileUniqueId = "small",
                Width = 90,
                Height = 90,
            },
            new PhotoSize
            {
                FileId = largest,
                FileUniqueId = "big",
                Width = 800,
                Height = 800,
            },
        ];
        return message;
    }
}
