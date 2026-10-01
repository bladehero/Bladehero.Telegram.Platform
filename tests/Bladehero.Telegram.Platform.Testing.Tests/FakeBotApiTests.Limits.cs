using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// Trimming and Telegram's limits on what the bot sends.
public sealed partial class FakeBotApiTests
{
    private const string Accepted = "accepted";

    [Theory]
    [InlineData(" ", null)]
    [InlineData("\n \t\n", null)]
    [InlineData("<b></b>", ParseMode.Html)]
    [InlineData("<b> </b>", ParseMode.Html)]
    public async Task SendMessage_WithWhitespaceOnlyOrEmptyMarkup_ShouldBeRefusedAsNonEmpty(
        string text,
        ParseMode? parseMode
    )
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, text, parseMode ?? default));

        // Assert
        outcome.Should().Be("Bad Request: text must be non-empty");
    }

    [Theory]
    [InlineData(4096, Accepted)]
    [InlineData(4097, "Bad Request: message is too long")]
    public async Task SendMessage_ByTextLength_ShouldBeLimitedLikeTelegram(int length, string expected)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act: the surrounding whitespace does not count.
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, $" {new string('a', length)}\n"));

        // Assert
        using (new AssertionScope())
        {
            outcome.Should().Be(expected);
            api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
            api.MessagesIn(Chat).Should().HaveCount(expected == Accepted ? 1 : 0);
        }
    }

    [Theory]
    [InlineData(4096, Accepted)]
    [InlineData(4097, "Bad Request: MESSAGE_TOO_LONG")]
    public async Task EditMessageText_ByTextLength_ShouldBeLimitedLikeTelegram(int length, string expected)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "Draft");

        // Act
        var outcome = await OutcomeOf(() => client.EditMessageText(Chat, sent.Id, new string('a', length)));

        // Assert
        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData("sendPhoto", 1024, Accepted)]
    [InlineData("sendPhoto", 1025, "Bad Request: message caption is too long")]
    [InlineData("sendDocument", 1024, Accepted)]
    [InlineData("sendDocument", 1025, "Bad Request: message caption is too long")]
    [InlineData("sendVoice", 1024, Accepted)]
    [InlineData("sendVoice", 1025, "Bad Request: message caption is too long")]
    public async Task SendFile_ByCaptionLength_ShouldBeLimitedLikeTelegram(string method, int length, string expected)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var outcome = await OutcomeOf(() => SendFileAsync(client, method, caption: new string('a', length)));

        // Assert
        using (new AssertionScope())
        {
            outcome.Should().Be(expected);
            api.Calls.Should().ContainSingle(x => x.Method == method);
            api.MessagesIn(Chat).Should().HaveCount(expected == Accepted ? 1 : 0);
        }
    }

    [Theory]
    [InlineData(1024, Accepted)]
    [InlineData(1025, "Bad Request: MEDIA_CAPTION_TOO_LONG")]
    public async Task EditMessageCaption_ByCaptionLength_ShouldBeLimitedLikeTelegram(int length, string expected)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await SendFileAsync(client, "sendPhoto", caption: "A cat");

        // Act
        var outcome = await OutcomeOf(() => client.EditMessageCaption(Chat, sent.Id, new string('a', length)));

        // Assert
        outcome.Should().Be(expected);
    }

    // Cyrillic letters are two UTF-8 bytes each: 32 of them are Telegram's 64 bytes.
    [Theory]
    [InlineData("sendMessage", 32, Accepted)]
    [InlineData("sendMessage", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("sendMessage", 0, "Bad Request: text buttons are not allowed in the inline keyboard")]
    [InlineData("sendPhoto", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("sendDocument", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("sendVoice", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("editMessageText", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("editMessageCaption", 33, "Bad Request: BUTTON_DATA_INVALID")]
    [InlineData("editMessageReplyMarkup", 33, "Bad Request: BUTTON_DATA_INVALID")]
    public async Task InlineKeyboard_ByCallbackDataLength_ShouldBeLimitedLikeTelegram(
        string method,
        int cyrillicLetters,
        string expected
    )
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var keyboard = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData("Pick", new string('я', cyrillicLetters))
        );

        // Act
        var outcome = await OutcomeOf(() => WithKeyboardAsync(client, method, keyboard));

        // Assert
        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData("sendMessage")]
    [InlineData("sendPhoto")]
    [InlineData("sendDocument")]
    [InlineData("sendVoice")]
    [InlineData("editMessageText")]
    [InlineData("editMessageCaption")]
    [InlineData("editMessageReplyMarkup")]
    public async Task InlineKeyboard_WithAButtonThatDoesNothing_ShouldFailLikeTelegram(string method)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var keyboard = new InlineKeyboardMarkup(new InlineKeyboardButton("Just text"));

        // Act
        var outcome = await OutcomeOf(() => WithKeyboardAsync(client, method, keyboard));

        // Assert
        outcome.Should().Be("Bad Request: text buttons are not allowed in the inline keyboard");
    }

    [Theory]
    [InlineData("style", "primary")]
    [InlineData("icon_custom_emoji_id", "5368324170671202286")]
    public async Task InlineKeyboard_WithTextAndOnlyAStyleOrIcon_ShouldBeRefusedAsATextButton(
        string field,
        string value
    )
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var body = new JsonObject
        {
            ["chat_id"] = Chat,
            ["text"] = "Pick one",
            ["reply_markup"] = new JsonObject
            {
                ["inline_keyboard"] = new JsonArray(
                    new JsonArray(new JsonObject { ["text"] = "Pick", [field] = value })
                ),
            },
        };

        // Act
        var outcome = await OutcomeOf(() => client.SendRequest(new RawRequest<Message>("sendMessage", body)));

        // Assert
        outcome.Should().Be("Bad Request: text buttons are not allowed in the inline keyboard");
    }

    [Theory]
    [InlineData(200, Accepted)]
    [InlineData(201, "Bad Request: MESSAGE_TOO_LONG")]
    public async Task AnswerCallbackQuery_ByTextLength_ShouldBeLimitedLikeTelegram(int length, string expected)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Enqueue(new JsonObject { ["callback_query"] = new JsonObject { ["id"] = "7" } });

        // Act
        var outcome = await OutcomeOf(() => client.AnswerCallbackQuery("7", new string('a', length)));

        // Assert
        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task SendMessage_WithAnEmptyInlineKeyboard_ShouldShowNoKeyboard()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var withoutRows = await client.SendMessage(Chat, "Continue?", replyMarkup: new InlineKeyboardMarkup());
        var withEmptyRows = await client.SendMessage(
            Chat,
            "Continue?",
            replyMarkup: new InlineKeyboardMarkup([
                [],
                [],
            ])
        );

        // Assert
        using (new AssertionScope())
        {
            withoutRows.ReplyMarkup.Should().BeNull();
            withEmptyRows.ReplyMarkup.Should().BeNull();
        }
    }

    [Fact]
    public async Task EditMessageReplyMarkup_WithAnEmptyInlineKeyboard_ShouldRemoveTheButtons()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: YesNo);

        // Act
        var edited = await client.EditMessageReplyMarkup(Chat, sent.Id, new InlineKeyboardMarkup());

        // Assert
        edited.ReplyMarkup.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_WithSurroundingWhitespace_ShouldBeTrimmedLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "\n  Continue? \n");

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("Continue?");
            api.MessagesIn(Chat).Single()["text"]!.GetValue<string>().Should().Be("Continue?");
        }
    }

    [Fact]
    public async Task EditMessageText_ToTheSameTextWithATrailingNewline_ShouldNotBeModified()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?");

        // Act
        var outcome = await OutcomeOf(() => client.EditMessageText(Chat, sent.Id, "Continue?\n"));

        // Assert
        outcome
            .Should()
            .Be(
                "Bad Request: message is not modified: specified new message content and reply markup are exactly "
                    + "the same as a current content and reply markup of the message"
            );
    }

    [Fact]
    public async Task SendMessage_WithLeadingWhitespace_ShouldShiftTheEntities()
    {
        // Arrange: "hi" in bold and "hi there  " in italic; no entity covers the leading spaces, so they go.
        var client = ApiWithChats().CreateClient();
        MessageEntity[] entities =
        [
            new()
            {
                Type = MessageEntityType.Bold,
                Offset = 2,
                Length = 2,
            },
            new()
            {
                Type = MessageEntityType.Italic,
                Offset = 2,
                Length = 10,
            },
        ];

        // Act
        var sent = await client.SendMessage(Chat, "  hi there  ", entities: entities);

        // Assert
        // In Telegram's order: by offset, longer first.
        sent.Entities!.Select(x => $"{x.Type} {x.Offset}+{x.Length}").Should().Equal("Italic 0+8", "Bold 0+2");
    }

    [Theory]
    [InlineData("  A cat \n", "A cat")]
    [InlineData(" \n ", null)]
    public async Task SendPhoto_WithACaptionInWhitespace_ShouldTrimItOrLeaveItOut(string caption, string? expected)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await SendFileAsync(client, "sendPhoto", caption);

        // Assert
        sent.Caption.Should().Be(expected);
    }

    [Fact]
    public async Task InlineKeyboard_ButtonsWithEmptyText_ShouldBeDroppedSilently()
    {
        // Arrange: a row with an empty button beside a real one, and a row of empty buttons only.
        var client = ApiWithChats().CreateClient();
        InlineKeyboardMarkup keyboard = new([
            [InlineKeyboardButton.WithCallbackData("", "gone"), InlineKeyboardButton.WithCallbackData("A", "a")],
            [InlineKeyboardButton.WithCallbackData("", "gone too")],
        ]);

        // Act
        var sent = await client.SendMessage(Chat, "Pick one", replyMarkup: keyboard);
        var none = await client.SendMessage(
            Chat,
            "Pick none",
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("", "x"))
        );

        // Assert
        using (new AssertionScope())
        {
            sent.ReplyMarkup!.InlineKeyboard.Select(row => string.Join(",", row.Select(x => x.Text)))
                .Should()
                .Equal("A");
            none.ReplyMarkup.Should().BeNull();
        }
    }

    [Fact]
    public async Task InlineKeyboard_RowsLongerThan12_ShouldBeCut()
    {
        // Arrange: 26 rows of 13 buttons, 338 in all.
        var client = ApiWithChats().CreateClient();
        var keyboard = new InlineKeyboardMarkup(
            Enumerable
                .Range(0, 26)
                .Select(row =>
                    Enumerable.Range(0, 13).Select(column => InlineKeyboardButton.WithCallbackData($"{row}.{column}"))
                )
        );

        // Act
        var sent = await client.SendMessage(Chat, "Pick one", replyMarkup: keyboard);

        // Assert: 12 a row, and 300 in all.
        var rows = sent.ReplyMarkup!.InlineKeyboard.Select(row => row.Count()).ToArray();
        using (new AssertionScope())
        {
            rows.Should().HaveCount(25).And.AllSatisfy(count => count.Should().Be(12));
            rows.Sum().Should().Be(300);
        }
    }

    private static async Task<string> OutcomeOf(Func<Task> call)
    {
        try
        {
            await call();
            return Accepted;
        }
        catch (ApiRequestException exception)
        {
            return exception.Message;
        }
    }

    private static InputFile Upload(string fileName = "file") =>
        InputFile.FromStream(new MemoryStream("bytes"u8.ToArray()), fileName);

    private static Task<Message> SendFileAsync(ITelegramBotClient client, string method, string caption) =>
        method switch
        {
            "sendPhoto" => client.SendPhoto(Chat, Upload(), caption: caption),
            "sendDocument" => client.SendDocument(Chat, Upload("report.csv"), caption: caption),
            _ => client.SendVoice(Chat, Upload(), caption: caption),
        };

    // Calls the method with the keyboard, first sending the message an edit needs.
    private static async Task WithKeyboardAsync(ITelegramBotClient client, string method, InlineKeyboardMarkup keyboard)
    {
        switch (method)
        {
            case "sendMessage":
                await client.SendMessage(Chat, "Pick one", replyMarkup: keyboard);
                break;
            case "sendPhoto":
                await client.SendPhoto(Chat, Upload(), replyMarkup: keyboard);
                break;
            case "sendDocument":
                await client.SendDocument(Chat, Upload("report.csv"), replyMarkup: keyboard);
                break;
            case "sendVoice":
                await client.SendVoice(Chat, Upload(), replyMarkup: keyboard);
                break;
            case "editMessageText":
                var text = await client.SendMessage(Chat, "Pick one");
                await client.EditMessageText(Chat, text.Id, "Pick again", replyMarkup: keyboard);
                break;
            case "editMessageCaption":
                var photo = await client.SendPhoto(Chat, Upload(), caption: "A cat");
                await client.EditMessageCaption(Chat, photo.Id, "A dog", replyMarkup: keyboard);
                break;
            default:
                var sent = await client.SendMessage(Chat, "Pick one");
                await client.EditMessageReplyMarkup(Chat, sent.Id, keyboard);
                break;
        }
    }
}
