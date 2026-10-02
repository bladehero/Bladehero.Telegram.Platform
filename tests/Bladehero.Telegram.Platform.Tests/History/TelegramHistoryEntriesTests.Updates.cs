using System.Text.Json;
using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed partial class TelegramHistoryEntriesTests
{
    private const long Anna = 7000000002;

    private const string TapOnAMessage = """
        {"update_id":5,"callback_query":{"id":"cb1","chat_instance":"ci","data":"size:large",
         "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
         "message":{"message_id":8,"date":1700000000,"text":"What size?",
          "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"}}}}
        """;

    [Fact]
    public void FromUpdate_TextMessageInAGroup_ShouldRecordTheChatUserMessageAndText()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":1,"message":{"message_id":5,"date":1700000000,"text":"/coffee",
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Time = Now,
                    Direction = TelegramHistoryDirection.Incoming,
                    Kind = "message",
                    UpdateId = 1,
                    ChatId = Group,
                    UserId = Nick,
                    MessageId = 5,
                    Text = "/coffee",
                }
            );
    }

    [Fact]
    public void FromUpdate_PhotoWithACaption_ShouldRecordTheCaptionAndTheLargestSize()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":2,"message":{"message_id":6,"date":1700000000,"caption":"receipt",
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
             "photo":[{"file_id":"small","file_unique_id":"a","width":90,"height":90},
                      {"file_id":"big","file_unique_id":"b","width":800,"height":800}]}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = "receipt", FileId = "big" });
    }

    [Fact]
    public void FromUpdate_Document_ShouldRecordTheFileIdAndName()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":3,"message":{"message_id":7,"date":1700000000,
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
             "document":{"file_id":"BQAD","file_unique_id":"c","file_name":"receipts.csv"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { FileId = "BQAD", FileName = "receipts.csv" });
    }

    [Fact]
    public void FromUpdate_EditedMessage_ShouldRecordTheNewText()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":3,"edited_message":{"message_id":5,"date":1700000000,"edit_date":1700000100,
             "text":"/coffee large",
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "edited_message",
                    MessageId = 5,
                    Text = "/coffee large",
                }
            );
    }

    [Fact]
    public void FromUpdate_ChannelPost_ShouldHaveNoUser()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":4,"channel_post":{"message_id":7,"date":1700000000,"text":"post",
             "chat":{"id":-1009876543210,"type":"channel","title":"News"},
             "sender_chat":{"id":-1009876543210,"type":"channel","title":"News"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Channel, UserId = (long?)null });
    }

    [Fact]
    public void FromUpdate_CallbackQueryOnAMessage_ShouldTakeTheMessagesChat()
    {
        // Arrange
        var update = Parse(TapOnAMessage);

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "callback_query",
                    ChatId = Group,
                    UserId = Nick,
                    MessageId = 8,
                    Text = "size:large",
                }
            );
    }

    [Fact]
    public void FromUpdate_CallbackQueryOnAnInlineMessage_ShouldHaveTheInlineIdAndNoChat()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":6,"callback_query":{"id":"cb2","chat_instance":"ci","data":"inline:1",
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},"inline_message_id":"AAAAinline"}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    InlineMessageId = "AAAAinline",
                    ChatId = (long?)null,
                    MessageId = (int?)null,
                }
            );
    }

    [Fact]
    public void FromUpdate_CallbackQueryOnAnInaccessibleMessage_ShouldKeepTheChatAndMessage()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":7,"callback_query":{"id":"cb3","chat_instance":"ci","data":"old",
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
             "message":{"message_id":9,"date":0,"chat":{"id":-1001234567890,"type":"supergroup","title":"Family"}}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Group, MessageId = 9 });
    }

    [Fact]
    public void FromUpdate_InlineQuery_ShouldRecordTheUserAndTheQuery()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":8,"inline_query":{"id":"iq","query":"lat","offset":"",
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    UserId = Nick,
                    Text = "lat",
                    ChatId = (long?)null,
                }
            );
    }

    [Fact]
    public void FromUpdate_ChosenInlineResult_ShouldRecordTheInlineIdAndTheQuery()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":9,"chosen_inline_result":{"result_id":"r","query":"latte","inline_message_id":"AAAAchosen",
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    UserId = Nick,
                    InlineMessageId = "AAAAchosen",
                    Text = "latte",
                }
            );
    }

    [Fact]
    public void FromUpdate_PollAnswerByAUser_ShouldHaveOnlyTheUser()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":10,"poll_answer":{"poll_id":"p","option_ids":[1],
             "user":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { UserId = Nick, ChatId = (long?)null });
    }

    [Fact]
    public void FromUpdate_PollAnswerByAChannel_ShouldHaveOnlyTheChat()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":11,"poll_answer":{"poll_id":"p","option_ids":[0],
             "voter_chat":{"id":-1009876543210,"type":"channel","title":"News"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Channel, UserId = (long?)null });
    }

    [Fact]
    public void FromUpdate_BusinessMessage_ShouldRecordTheChatAndTheSender()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":12,"business_message":{"message_id":10,"date":1700000000,"business_connection_id":"bc",
             "text":"hello shop",
             "chat":{"id":7000000002,"type":"private","first_name":"Anna"},
             "from":{"id":7000000002,"is_bot":false,"first_name":"Anna"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "business_message",
                    ChatId = Anna,
                    UserId = Anna,
                    MessageId = 10,
                }
            );
    }

    [Fact]
    public void FromUpdate_DeletedBusinessMessages_ShouldRecordEachMessage()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":13,"deleted_business_messages":{"business_connection_id":"bc","message_ids":[10,11],
             "chat":{"id":7000000002,"type":"private","first_name":"Anna"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        using (new AssertionScope())
        {
            entries.Select(x => x.MessageId).Should().Equal(10, 11);
            entries.Should().OnlyContain(x => x.UpdateId == 13 && x.ChatId == Anna);
        }
    }

    [Fact]
    public void FromUpdate_BusinessConnection_ShouldTakeTheUsersChat()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":14,"business_connection":{"id":"bc","user_chat_id":7000000001,"date":1700000000,
             "can_reply":true,"is_enabled":true,"user":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Nick, UserId = Nick });
    }

    [Fact]
    public void FromUpdate_MessageReaction_ShouldRecordTheChatUserAndMessage()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":15,"message_reaction":{"message_id":8,"date":1700000000,
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "user":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
             "old_reaction":[],"new_reaction":[{"type":"emoji","emoji":"👍"}]}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    ChatId = Group,
                    UserId = Nick,
                    MessageId = 8,
                }
            );
    }

    [Fact]
    public void FromUpdate_MyChatMember_ShouldRecordTheChatAndTheUser()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":16,"my_chat_member":{"date":1700000000,
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"},
             "old_chat_member":{"status":"left","user":{"id":1234567,"is_bot":true,"first_name":"Bot"}},
             "new_chat_member":{"status":"member","user":{"id":1234567,"is_bot":true,"first_name":"Bot"}}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Group, UserId = Nick });
    }

    [Fact]
    public void FromUpdate_PreCheckoutQuery_ShouldHaveOnlyTheUser()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":17,"pre_checkout_query":{"id":"pc","currency":"EUR","total_amount":250,
             "invoice_payload":"coffee","from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new { UserId = Nick, ChatId = (long?)null });
    }

    [Fact]
    public void FromUpdate_AnonymousAdminsMessage_ShouldKeepTelegramsPlaceholderUser()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":18,"message":{"message_id":12,"date":1700000000,"text":"anon admin",
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":1087968824,"is_bot":true,"first_name":"Group","username":"GroupAnonymousBot"},
             "sender_chat":{"id":-1001234567890,"type":"supergroup","title":"Family"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.UserId.Should().Be(1087968824);
    }

    [Fact]
    public void FromUpdate_OfAnUnknownType_ShouldRecordOnlyTheKind()
    {
        // Arrange
        var update = Parse("""{"update_id":19,"some_future_update":{"x":1}}""");

        // Act
        var entries = Incoming(update);

        // Assert
        entries
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "unknown",
                    UpdateId = 19,
                    ChatId = (long?)null,
                    UserId = (long?)null,
                    MessageId = (int?)null,
                    Text = (string?)null,
                }
            );
    }

    [Fact]
    public void FromUpdate_WithoutKeepJson_ShouldLeaveTheJsonOut()
    {
        // Arrange
        var update = Parse(TapOnAMessage);

        // Act
        var entries = TelegramHistoryEntries.FromUpdate(update, Now, keepJson: false);

        // Assert
        entries.Should().ContainSingle().Which.Json.Should().BeNull();
    }

    [Fact]
    public void FromUpdate_CyrillicText_ShouldKeepItReadableInTheJson()
    {
        // Arrange
        var update = Parse(
            """
            {"update_id":20,"message":{"message_id":13,"date":1700000000,"text":"Кава велика",
             "chat":{"id":-1001234567890,"type":"supergroup","title":"Family"},
             "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
            """
        );

        // Act
        var entries = Incoming(update);

        // Assert
        entries.Should().ContainSingle().Which.Json.Should().Contain("\"text\":\"Кава велика\"");
    }

    private static Update Parse(string json) => JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!;

    private static IReadOnlyList<TelegramHistoryEntry> Incoming(Update update) =>
        TelegramHistoryEntries.FromUpdate(update, Now, keepJson: true);
}
