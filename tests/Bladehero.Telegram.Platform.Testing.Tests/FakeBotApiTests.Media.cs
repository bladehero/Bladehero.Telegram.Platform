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
    public async Task SendMediaGroup_ShouldPostOneMessagePerItemSharingAMediaGroupId()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var sent = await api.CreateClient()
            .SendMediaGroup(
                Chat,
                [new InputMediaPhoto(Jpeg("front")) { Caption = "Receipt" }, new InputMediaPhoto(Jpeg("back"))]
            );

        // Assert
        using (new AssertionScope())
        {
            sent.Should().HaveCount(2);
            sent.Select(x => x.MediaGroupId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
            sent.Select(x => x.Caption).Should().Equal("Receipt", null);
            api.MessagesIn(Chat).Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task SendMediaGroup_WithOneItem_ShouldSendANormalMessage()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var sent = await api.CreateClient().SendMediaGroup(Chat, [new InputMediaPhoto(Jpeg("front"))]);

        // Assert
        using (new AssertionScope())
        {
            sent.Should().ContainSingle().Which.MediaGroupId.Should().BeNull();
            sent[0].Photo.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("absent", 400, "Bad Request: parameter \"media\" is required")]
    [InlineData("empty", 400, "Bad Request: there are no messages to send")]
    [InlineData("eleven", 400, "Bad Request: too many messages to send as an album")]
    [InlineData(
        "animation",
        400,
        "Bad Request: can't parse InputMedia: type \"animation\" can't be used in sendMediaGroup"
    )]
    [InlineData("sticker", 400, "Bad Request: can't parse InputMedia: type \"sticker\" is unsupported")]
    [InlineData("video", 404, "Not Found: FakeBotApi does not support audio and video yet")]
    [InlineData("unattached", 400, "Bad Request: can't parse InputMedia: media not found")]
    [InlineData("mixed", 400, "Bad Request: document can't be mixed with other media types")]
    public async Task SendMediaGroup_ShouldBeRefusedLikeTelegram(string media, int errorCode, string description)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var photo = await PhotoIdAsync(client);
        JsonObject Item(string type, string file) => new() { ["type"] = type, ["media"] = file };
        var body = new JsonObject { ["chat_id"] = Chat };
        body["media"] = media switch
        {
            "absent" => null,
            "empty" => new JsonArray(),
            "eleven" => new JsonArray([.. Enumerable.Range(0, 11).Select(_ => (JsonNode)Item("photo", photo))]),
            "unattached" => new JsonArray(Item("photo", photo), Item("photo", "attach://nothing")),
            "mixed" => new JsonArray(Item("photo", photo), Item("document", photo)),
            _ => new JsonArray(Item("photo", photo), Item(media, photo)),
        };

        // Act
        var act = () => client.SendRequest(new RawRequest<Message[]>("sendMediaGroup", body));

        // Assert
        var refused = (await act.Should().ThrowAsync<ApiRequestException>()).Which;
        using (new AssertionScope())
        {
            refused.ErrorCode.Should().Be(errorCode);
            refused.Message.Should().Be(description);
            api.MessagesIn(Chat).Should().ContainSingle("only the photo sent for its id is there");
        }
    }

    [Fact]
    public async Task SendMediaGroup_ShouldIgnoreAReplyMarkup()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var photo = await PhotoIdAsync(client);
        var body = new JsonObject
        {
            ["chat_id"] = Chat,
            ["media"] = new JsonArray(
                new JsonObject { ["type"] = "photo", ["media"] = photo },
                new JsonObject { ["type"] = "photo", ["media"] = photo }
            ),
            ["reply_markup"] = JsonNode.Parse("""{"inline_keyboard":[[{"text":"A","callback_data":"a"}]]}"""),
        };

        // Act
        var sent = await client.SendRequest(new RawRequest<Message[]>("sendMediaGroup", body));

        // Assert
        sent.Should().AllSatisfy(x => x.ReplyMarkup.Should().BeNull());
    }

    [Fact]
    public async Task EditMessageMedia_ShouldReplaceThePhotoAndItsCaption()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var cat = await client.SendPhoto(
            Chat,
            Jpeg("cat"),
            caption: "A cat",
            replyMarkup: InlineKeyboardButton.WithCallbackData("Again", "again")
        );

        // Act
        var dog = await client.EditMessageMedia(Chat, cat.Id, new InputMediaPhoto(Jpeg("dog")) { Caption = "A dog" });

        // Assert
        using (new AssertionScope())
        {
            dog.Caption.Should().Be("A dog");
            dog.Photo![^1].FileId.Should().NotBe(cat.Photo![^1].FileId);
            dog.ReplyMarkup.Should().BeNull();
            dog.EditDate.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task EditMessageMedia_OnATextMessage_ShouldMakeItMedia()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var text = await client.SendMessage(Chat, "Your report is coming");

        // Act
        var report = await client.EditMessageMedia(
            Chat,
            text.Id,
            new InputMediaDocument(InputFile.FromStream(new MemoryStream("a,b"u8.ToArray()), "report.csv"))
        );

        // Assert
        using (new AssertionScope())
        {
            report.Text.Should().BeNull();
            report.Document!.FileName.Should().Be("report.csv");
        }
    }

    [Theory]
    [InlineData("voice", "Bad Request: message media can't be edited")]
    [InlineData("a user's", "Bad Request: message media can't be edited")]
    [InlineData("missing", "Bad Request: message to edit not found")]
    [InlineData("album type", "Bad Request: can't change media type in the album")]
    [InlineData("album content", "Bad Request: message content type can't be used in an album")]
    [InlineData("identical", NotModifiedText)]
    public async Task EditMessageMedia_ShouldBeRefusedLikeTelegram(string target, string description)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var photo = await client.SendPhoto(Chat, Jpeg("cat"));
        var photoId = photo.Photo![^1].FileId;
        var album = await client.SendMediaGroup(Chat, [new InputMediaPhoto(photoId), new InputMediaPhoto(photoId)]);
        var voice = await client.SendVoice(Chat, InputFile.FromStream(new MemoryStream("ogg"u8.ToArray()), "hi.ogg"));
        var users = api.Receive(Chat, Nick(), "hello")["message_id"]!.GetValue<int>();
        var (messageId, media) = target switch
        {
            "voice" => (voice.Id, new JsonObject { ["type"] = "photo", ["media"] = photoId }),
            "a user's" => (users, new JsonObject { ["type"] = "photo", ["media"] = photoId }),
            "missing" => (999, new JsonObject { ["type"] = "photo", ["media"] = photoId }),
            "album type" => (album[0].Id, new JsonObject { ["type"] = "document", ["media"] = photoId }),
            "album content" => (album[0].Id, new JsonObject { ["type"] = "video", ["media"] = photoId }),
            _ => (photo.Id, new JsonObject { ["type"] = "photo", ["media"] = photoId }),
        };

        // Act
        var act = () =>
            client.SendRequest(
                new RawRequest<Message>(
                    "editMessageMedia",
                    new JsonObject
                    {
                        ["chat_id"] = Chat,
                        ["message_id"] = messageId,
                        ["media"] = media,
                    }
                )
            );

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be(description);
    }

    [Fact]
    public async Task EditMessageMedia_WhenRefused_ShouldLeaveTheMessageAsItWas()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var cat = await client.SendPhoto(Chat, Jpeg("cat"), caption: "A cat");
        var before = api.MessagesIn(Chat).Single().ToJsonString();

        // Act
        var act = () =>
            client.EditMessageMedia(Chat, cat.Id, new InputMediaPhoto("no-such-file") { Caption = "A dog" });

        // Assert
        using (new AssertionScope())
        {
            (await act.Should().ThrowAsync<ApiRequestException>())
                .Which.Message.Should()
                .Be("Bad Request: wrong file identifier/HTTP URL specified");
            api.MessagesIn(Chat).Single().ToJsonString().Should().Be(before);
        }
    }

    [Fact]
    public async Task SendMediaGroup_WhenALaterItemIsRefused_ShouldPostNothing()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var act = () =>
            api.CreateClient()
                .SendMediaGroup(Chat, [new InputMediaPhoto(Jpeg("front")), new InputMediaPhoto("no-such-file")]);

        // Assert
        using (new AssertionScope())
        {
            (await act.Should().ThrowAsync<ApiRequestException>())
                .Which.Message.Should()
                .Be("Bad Request: wrong file identifier/HTTP URL specified");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    private const string NotModifiedText =
        "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message";

    private static InputFile Jpeg(string name) =>
        InputFile.FromStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(name)), $"{name}.jpg");

    // The id of a photo the bot sent to the chat.
    private static async Task<string> PhotoIdAsync(ITelegramBotClient client) =>
        (await client.SendPhoto(Chat, Jpeg("cat"))).Photo![^1].FileId;

    // A request as JSON, for what Telegram.Bot's own methods can't send.
    private sealed class RawRequest<TResponse>(string method, JsonObject body) : RequestBase<TResponse>(method)
    {
        public override HttpContent ToHttpContent() => JsonContent.Create(body);
    }
}
