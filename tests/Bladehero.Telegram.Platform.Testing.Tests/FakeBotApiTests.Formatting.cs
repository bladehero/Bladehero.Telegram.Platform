using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Theory]
    [InlineData("Total: <b>5</b>", ParseMode.Html)]
    [InlineData("Total: *5*", ParseMode.MarkdownV2)]
    [InlineData("Total: *5*", ParseMode.Markdown)]
    public async Task SendMessage_WithParseMode_ShouldStoreThePlainTextAndItsEntities(string text, ParseMode parseMode)
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var sent = await api.CreateClient().SendMessage(Chat, text, parseMode);

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("Total: 5");
            sent.Entities!.Select(x => $"{x.Type} {x.Offset}+{x.Length}").Should().Equal("Bold 7+1");
            api.MessagesIn(Chat).Single()["text"]!.GetValue<string>().Should().Be("Total: 5");
        }
    }

    [Theory]
    [InlineData("HTML")]
    [InlineData("html")]
    public async Task SendMessage_WithParseModeInAnyCase_ShouldParse(string parseMode)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendRequest(
            new RawRequest<Message>(
                "sendMessage",
                new JsonObject
                {
                    ["chat_id"] = Chat,
                    ["text"] = "<b>5</b>",
                    ["parse_mode"] = parseMode,
                }
            )
        );

        // Assert
        sent.Text.Should().Be("5");
    }

    [Fact]
    public async Task SendMessage_WithAnUnknownParseMode_ShouldBeRefused()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() =>
            client.SendRequest(
                new RawRequest<Message>(
                    "sendMessage",
                    new JsonObject
                    {
                        ["chat_id"] = Chat,
                        ["text"] = "<b>5</b>",
                        ["parse_mode"] = "Html5",
                    }
                )
            )
        );

        // Assert
        outcome.Should().Be("Bad Request: unsupported parse_mode");
    }

    [Fact]
    public async Task SendMessage_WithParseModeAndEntities_ShouldIgnoreTheEntities()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendMessage(
            Chat,
            "<b>5</b> apples",
            ParseMode.Html,
            entities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Italic,
                    Offset = 0,
                    Length = 3,
                },
            ]
        );

        // Assert
        sent.Entities!.Select(x => $"{x.Type} {x.Offset}+{x.Length}").Should().Equal("Bold 0+1");
    }

    [Fact]
    public async Task SendMessage_WithAnEmptyText_ShouldBeRefusedAsEmpty()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() =>
            client.SendRequest(
                new RawRequest<Message>("sendMessage", new JsonObject { ["chat_id"] = Chat, ["text"] = "" })
            )
        );

        // Assert
        outcome.Should().Be("Bad Request: message text is empty");
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u00A0")]
    [InlineData("\u2800")]
    [InlineData("\u3000")]
    [InlineData("\uFEFF")]
    [InlineData("\u200B  \n")]
    public async Task SendMessage_WithOnlyInvisibleCharacters_ShouldBeRefusedAsNonEmpty(string text)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, text));

        // Assert
        outcome.Should().Be("Bad Request: text must be non-empty");
    }

    [Fact]
    public async Task SendMessage_WithAHangulFiller_ShouldBeAccepted()
    {
        // Arrange: U+3164 isn't among TDLib's empty characters.
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "\u3164");

        // Assert
        sent.Text.Should().Be("\u3164");
    }

    [Fact]
    public async Task SendPhoto_WithAnInvisibleCaption_ShouldKeepIt()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendPhoto(Chat, Upload("cat.jpg"), caption: "\u200B");

        // Assert
        sent.Caption.Should().Be("\u200B");
    }

    [Fact]
    public async Task SendMessage_WithAnyParseMode_ShouldReportAParseErrorWithItsByteOffset()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, "Сумма: <b>5", ParseMode.Html));

        // Assert
        outcome.Should().Be("Bad Request: can't parse entities: Can't find end tag corresponding to start tag \"b\"");
    }

    [Fact]
    public async Task SendMessage_OverTheRawByteLimit_ShouldBeRefused()
    {
        // Arrange: 8193 four-byte emoji are 32772 bytes, and only 8193 characters.
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, string.Concat(Enumerable.Repeat("😀", 8193))));

        // Assert
        outcome.Should().Be("Bad Request: text is too long");
    }

    [Fact]
    public async Task SendMessage_With4096CharactersPlusMarkup_ShouldBeAccepted()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, $"<b>{new string('a', 4096)}</b>", ParseMode.Html);

        // Assert
        sent.Text.Should().HaveLength(4096);
    }

    [Theory]
    [InlineData(4096, Accepted)]
    [InlineData(4097, "Bad Request: message is too long")]
    public async Task SendMessage_ByEmojiCount_ShouldCountCharactersNotUtf16Units(int emoji, string expected)
    {
        // Arrange: each emoji is two UTF-16 units.
        var client = ApiWithChats().CreateClient();

        // Act
        var outcome = await OutcomeOf(() => client.SendMessage(Chat, string.Concat(Enumerable.Repeat("😀", emoji))));

        // Assert
        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task EditMessageText_SameTextInAnotherMarkup_ShouldBeNotModified()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "Total: <b>5</b>", ParseMode.Html);

        // Act
        var outcome = await OutcomeOf(() => client.EditMessageText(Chat, sent.Id, "Total: *5*", ParseMode.MarkdownV2));

        // Assert
        outcome.Should().StartWith("Bad Request: message is not modified");
    }

    [Fact]
    public async Task SendPhoto_CaptionWithParseMode_ShouldBeRendered()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendPhoto(Chat, Upload("cat.jpg"), caption: "<i>A cat</i>", parseMode: ParseMode.Html);

        // Assert
        using (new AssertionScope())
        {
            sent.Caption.Should().Be("A cat");
            sent.CaptionEntities!.Select(x => $"{x.Type} {x.Offset}+{x.Length}").Should().Equal("Italic 0+5");
        }
    }

    [Fact]
    public async Task SendMediaGroup_And_CopyMessage_CaptionsWithParseMode_ShouldBeRendered()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var album = await client.SendMediaGroup(
            Chat,
            [
                new InputMediaPhoto(Upload("front.jpg")) { Caption = "<b>Front</b>", ParseMode = ParseMode.Html },
                new InputMediaPhoto(Upload("back.jpg")),
            ]
        );
        await client.CopyMessage(7, Chat, album[0].Id, caption: "*Copy*", parseMode: ParseMode.MarkdownV2);

        // Assert
        var copy = api.MessagesIn(7).Single();
        using (new AssertionScope())
        {
            album[0].Caption.Should().Be("Front");
            album[0].CaptionEntities.Should().ContainSingle().Which.Type.Should().Be(MessageEntityType.Bold);
            copy["caption"]!.GetValue<string>().Should().Be("Copy");
            copy["caption_entities"]!.AsArray().Should().ContainSingle();
        }
    }
}
