using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    private const long Chat = 42;

    private static readonly InlineKeyboardMarkup YesNo = new([
        [InlineKeyboardButton.WithCallbackData("Yes", "yes"), InlineKeyboardButton.WithCallbackData("No", "no")],
    ]);

    private static readonly MessageEntity Bold = new()
    {
        Type = MessageEntityType.Bold,
        Offset = 2,
        Length = 3,
    };

    [Fact]
    public async Task SendMessage_ShouldAnswerWithTheMessageAsTelegramWould()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

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
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "Continue?", replyMarkup: new ReplyKeyboardMarkup("Yes"));

        // Assert
        sent.ReplyMarkup.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_ShouldNumberMessagesPerChat()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

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
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var sent = await client.SendMessage(Chat, "Continue?");
        await client.EditMessageText(Chat, sent.Id, "Done");

        // Assert
        using (new AssertionScope())
        {
            api.Calls.Select(x => x.Method).Should().Equal("sendMessage", "editMessageText");
            api.Calls[0].Parameters["text"]!.GetValue<string>().Should().Be("Continue?");
            api.Calls[1].Parameters["text"]!.GetValue<string>().Should().Be("Done");
        }
    }

    [Fact]
    public async Task Calls_WhenAReadCallIsChanged_ShouldKeepTheRecordAsSent()
    {
        // Arrange
        var api = ApiWithChats();
        await api.CreateClient().SendMessage(Chat, "Continue?");
        api.Calls[0].Parameters["text"] = "changed";

        // Act
        var text = api.Calls[0].Parameters["text"]!.GetValue<string>();

        // Assert
        text.Should().Be("Continue?");
    }

    [Fact]
    public async Task EditMessageText_ShouldReplaceTheTextAndKeyboard()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
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
        var client = ApiWithChats().CreateClient();
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
        var client = ApiWithChats().CreateClient();
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
        var api = ApiWithChats();
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
        var client = ApiWithChats().CreateClient();
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
        var client = ApiWithChats().CreateClient();
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
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.DeleteMessage(Chat, 99);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message to delete not found");
    }

    [Fact]
    public async Task AnswerCallbackQuery_ForAQueryTelegramNeverSent_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.AnswerCallbackQuery("unknown");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("query ID is invalid");
    }

    [Fact]
    public async Task AnswerCallbackQuery_Twice_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Enqueue(new JsonObject { ["callback_query"] = new JsonObject { ["id"] = "7" } });
        await client.AnswerCallbackQuery("7");

        // Act
        var act = () => client.AnswerCallbackQuery("7");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("query ID is invalid");
    }

    [Fact]
    public async Task SetMyCommands_ShouldKeepAMenuPerScope()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var privateChats = new BotCommandScopeAllPrivateChats();

        // Act
        await client.SetMyCommands([new BotCommand("start", "Start")]);
        await client.SetMyCommands([new BotCommand("help", "Help")], privateChats);

        // Assert
        using (new AssertionScope())
        {
            api.CommandMenu().Select(x => x.Command).Should().Equal("start");
            api.CommandMenu(privateChats).Select(x => x.Command).Should().Equal("help");
            (await client.GetMyCommands(privateChats)).Select(x => x.Command).Should().Equal("help");
        }
    }

    [Fact]
    public async Task Fail_ShouldRefuseTheMethodWithTheErrorAndChangeNothing()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        var act = () => client.SendMessage(Chat, "Continue?");

        // Assert
        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        using (new AssertionScope())
        {
            failure.Which.ErrorCode.Should().Be(403);
            failure.Which.Message.Should().Be("Forbidden: bot was blocked by the user");
            api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Fail_ForSomeTimes_ShouldAnswerAgainOnceTheyRunOut()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.ChatNotFound, times: 1);
        await ((Func<Task>)(() => client.SendMessage(Chat, "one"))).Should().ThrowAsync<ApiRequestException>();

        // Act
        var sent = await client.SendMessage(Chat, "two");

        // Assert
        sent.Text.Should().Be("two");
    }

    [Fact]
    public async Task Fail_WithTooManyRequests_ShouldLetTheClientWaitAndRetry()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.TooManyRequests(retryAfter: 1), times: 1);

        // Act
        var sent = await client.SendMessage(Chat, "Continue?");

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("Continue?");
            api.Calls.Where(x => x.Method == "sendMessage").Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Fail_ForOneChat_ShouldRefuseOnlyThatChat()
    {
        // Arrange: Anna (7) blocked the bot.
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendMessage", BotApiError.BotBlocked, chatId: 7);

        // Act
        var toAnna = await Record.ExceptionAsync(() => client.SendMessage(7, "Your limit is near"));
        var toNick = await client.SendMessage(Chat, "Your limit is near");

        // Assert
        using (new AssertionScope())
        {
            toAnna.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(403);
            toNick.Text.Should().Be("Your limit is near");
            api.MessagesIn(7).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Fail_ForOneChat_ShouldCountOnlyItsCalls()
    {
        // Arrange: uploads send chat_id as form text.
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Fail("sendPhoto", BotApiError.BotBlocked, times: 1, chatId: 7);
        await client.SendPhoto(Chat, InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())));

        // Act
        var first = await Record.ExceptionAsync(() =>
            client.SendPhoto(7, InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())))
        );
        var second = await client.SendPhoto(7, InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())));

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(403);
            second.Chat.Id.Should().Be(7);
        }
    }

    [Fact]
    public void Fail_OnGetUpdates_ShouldSayTheHostOwnsIt()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var act = () => api.Fail("getUpdates", BotApiError.ChatNotFound);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*test host*");
    }

    [Fact]
    public async Task DownloadFile_ShouldServeTheBytesTheUserSent()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var file = await client.GetFile(StoreDocument(api, "hello"u8.ToArray(), "hello.txt"));
        using var content = new MemoryStream();

        // Act
        await client.DownloadFile(file, content);

        // Assert
        content.ToArray().Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public async Task DownloadFile_WithAnEscapedPath_ShouldServeTheFile()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.GetFile(StoreDocument(api, "hello"u8.ToArray(), "hello.txt"));
        using var content = new MemoryStream();

        // Act
        await client.DownloadFile("documents/file%5F1.txt", content);

        // Assert
        content.ToArray().Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public async Task DownloadFile_WithAPathTelegramNeverGave_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var act = () => client.DownloadFile("documents/missing.txt", Stream.Null);

        // Assert
        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        using (new AssertionScope())
        {
            failure.Which.ErrorCode.Should().Be(404);
            failure.Which.Message.Should().Be("Not Found");
            api.Calls.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DownloadFile_BeforeGetFileGaveThePath_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        StoreDocument(api, "hello"u8.ToArray(), "hello.txt");

        // Act
        var act = () => client.DownloadFile("documents/file_1.txt", Stream.Null);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(404);
    }

    [Fact]
    public async Task GetFile_OfExactlyTwentyMegabytes_ShouldHandOutAPath()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, new byte[20 * 1024 * 1024], "big.zip");

        // Act
        var file = await client.GetFile(fileId);

        // Assert
        file.FilePath.Should().Be("documents/file_1.zip");
    }

    [Fact]
    public async Task GetFile_ForAFileTelegramDoesNotHave_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.GetFile("unknown");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("invalid file_id");
    }

    [Fact]
    public async Task GetFile_OverTheTwentyMegabytesBotsMayDownload_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, new byte[20 * 1024 * 1024 + 1], "big.zip");

        // Act
        var act = () => client.GetFile(fileId);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("file is too big");
    }

    [Fact]
    public async Task SendPhoto_Uploaded_ShouldKeepTheBytesAndReadTheFormFields()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray()), "cat.jpg"),
            caption: "123",
            replyMarkup: YesNo
        );

        // Assert
        using (new AssertionScope())
        {
            sent.Caption.Should().Be("123");
            sent.ReplyMarkup!.InlineKeyboard.SelectMany(row => row).Select(x => x.Text).Should().Equal("Yes", "No");
            api.TestFileOf(sent.Photo![^1].FileId).Content.Should().Equal("jpeg"u8.ToArray());
        }
    }

    [Fact]
    public async Task SendPhoto_WithAnEmptyUpload_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.SendPhoto(Chat, InputFile.FromStream(new MemoryStream(), "cat.jpg"));

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("file must be non-empty");
    }

    [Fact]
    public async Task SendDocument_ByAFileIdTelegramDoesNotHave_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.SendDocument(Chat, InputFile.FromFileId("unknown"));

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("wrong file identifier/HTTP URL specified");
    }

    [Fact]
    public async Task SendPhoto_WithADocumentsFileId_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, "a,b"u8.ToArray(), "report.csv");

        // Act
        var act = () => client.SendPhoto(Chat, InputFile.FromFileId(fileId));

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("type of file mismatch");
    }

    [Fact]
    public async Task EditMessageText_OnAPhoto_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(Chat, InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())));

        // Act
        var act = () => client.EditMessageText(Chat, sent.Id, "A cat");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("there is no text in the message to edit");
    }

    [Fact]
    public async Task EditMessageCaption_OnATextMessage_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "Continue?");

        // Act
        var act = () => client.EditMessageCaption(Chat, sent.Id, "A cat");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("there is no caption in the message to edit");
    }

    [Fact]
    public async Task EditMessageCaption_WithoutACaption_ShouldRemoveIt()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            caption: "A cat",
            captionEntities: [Bold]
        );

        // Act
        var edited = await client.EditMessageCaption(Chat, sent.Id, caption: null, captionEntities: [Bold]);

        // Assert
        using (new AssertionScope())
        {
            edited.Caption.Should().BeNull();
            edited.CaptionEntities.Should().BeNull();
        }
    }

    [Fact]
    public async Task SendPhoto_WithEntitiesButNoCaption_ShouldKeepNoEntities()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            captionEntities: [Bold]
        );

        // Assert
        sent.CaptionEntities.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_WithNoEntities_ShouldLeaveThemOutLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(Chat, "A cat", entities: []);

        // Act
        var act = () => client.EditMessageText(Chat, sent.Id, "A cat");

        // Assert
        using (new AssertionScope())
        {
            sent.Entities.Should().BeNull();
            (await act.Should().ThrowAsync<ApiRequestException>())
                .Which.Message.Should()
                .Contain("message is not modified");
        }
    }

    [Theory]
    [InlineData("Отчёт за май.pdf")]
    [InlineData("Café.txt")]
    [InlineData("😀 plan.pdf")]
    public async Task SendDocument_Uploaded_ShouldKeepTheFileNameAsWritten(string fileName)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var sent = await client.SendDocument(Chat, InputFile.FromStream(new MemoryStream("x"u8.ToArray()), fileName));

        // Assert
        using (new AssertionScope())
        {
            sent.Document!.FileName.Should().Be(fileName);
            api.TestFileOf(sent.Document.FileId).FileName.Should().Be(fileName);
        }
    }

    [Fact]
    public async Task SendDocument_WithAThumbnail_ShouldKeepEachUploadApart()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var sent = await client.SendDocument(
            Chat,
            InputFile.FromStream(new MemoryStream("a,b"u8.ToArray()), "report.csv"),
            thumbnail: InputFile.FromStream(new MemoryStream("thumb"u8.ToArray()), "report.csv")
        );

        // Assert
        api.TestFileOf(sent.Document!.FileId).ReadAsString().Should().Be("a,b");
    }

    [Fact]
    public void FileNameOf_ShouldPreferTheEncodedFileNameStar()
    {
        // Arrange
        var disposition = new ContentDispositionHeaderValue("form-data") { FileName = "plain.pdf" };
        disposition.FileNameStar = "Отчёт.pdf";

        // Act
        var fileName = FakeBotApi.FileNameOf(disposition);

        // Assert
        fileName.Should().Be("Отчёт.pdf");
    }

    [Theory]
    [InlineData("café.txt")] // Latin-1 bytes that are not UTF-8
    [InlineData("=?utf-8?B?SGk=?=")] // a literal name that looks like an RFC 2047 encoded word
    [InlineData("✓ done.txt")] // already decoded
    public void FileNameOf_WhenTheNameIsNotRawUtf8_ShouldKeepItAsWritten(string name)
    {
        // Arrange
        var disposition = new ContentDispositionHeaderValue("form-data");
        disposition.Parameters.Add(new NameValueHeaderValue("filename", $"\"{name}\""));

        // Act
        var fileName = FakeBotApi.FileNameOf(disposition);

        // Assert
        fileName.Should().Be(name);
    }

    [Fact]
    public async Task SendPhoto_Uploaded_ShouldRecordJsonFieldsAsJson()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            suggestedPostParameters: new SuggestedPostParameters { SendDate = DateTime.UtcNow.AddDays(1) }
        );

        // Assert
        api.Calls.Single(x => x.Method == "sendPhoto")
            .Parameters["suggested_post_parameters"]
            .Should()
            .BeOfType<JsonObject>();
    }

    [Fact]
    public async Task EditMessageCaption_ShouldReplaceTheCaptionsEntitiesWithIt()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            caption: "A cat",
            captionEntities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Bold,
                    Offset = 2,
                    Length = 3,
                },
            ]
        );

        // Act
        var edited = await client.EditMessageCaption(Chat, sent.Id, "Hi");

        // Assert
        edited.CaptionEntities.Should().BeNull();
    }

    [Fact]
    public async Task EditMessageCaption_ChangingOnlyTheEntities_ShouldBeAnEdit()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            caption: "A cat",
            captionEntities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Bold,
                    Offset = 2,
                    Length = 3,
                },
            ]
        );

        // Act
        var edited = await client.EditMessageCaption(
            Chat,
            sent.Id,
            "A cat",
            captionEntities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Italic,
                    Offset = 2,
                    Length = 3,
                },
            ]
        );

        // Assert
        edited.CaptionEntities.Should().ContainSingle().Which.Type.Should().Be(MessageEntityType.Italic);
    }

    [Fact]
    public async Task EditMessageText_ShouldReplaceTheTextsEntitiesWithIt()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendMessage(
            Chat,
            "A cat",
            entities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Bold,
                    Offset = 2,
                    Length = 3,
                },
            ]
        );

        // Act
        var edited = await client.EditMessageText(Chat, sent.Id, "A dog");

        // Assert
        using (new AssertionScope())
        {
            sent.Entities.Should().ContainSingle();
            edited.Entities.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("https://example.com/my%20report.pdf", "my report.pdf")]
    [InlineData("https://example.com/a%5Cb.pdf", "a\\b.pdf")]
    [InlineData("https://example.com/a%2Fb.pdf", "a/b.pdf")]
    [InlineData("https://example.com/", "file")]
    public async Task SendDocument_ByUrl_ShouldNameItAfterTheUrlsPath(string url, string fileName)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendDocument(Chat, InputFile.FromUri(url));

        // Assert
        sent.Document!.FileName.Should().Be(fileName);
    }

    [Fact]
    public async Task SendPhoto_WithCaptionEntities_ShouldKeepThem()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            caption: "A cat",
            captionEntities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.Bold,
                    Offset = 2,
                    Length = 3,
                },
            ]
        );

        // Assert
        sent.CaptionEntities.Should().ContainSingle().Which.Type.Should().Be(MessageEntityType.Bold);
    }

    [Fact]
    public async Task EditMessageText_OnAVoiceMessage_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendVoice(Chat, InputFile.FromStream(new MemoryStream("ogg"u8.ToArray())));

        // Act
        var act = () => client.EditMessageText(Chat, sent.Id, "Hello");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("there is no text in the message to edit");
    }

    [Fact]
    public async Task EditMessageCaption_OnADocument_ShouldReplaceTheCaption()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendDocument(
            Chat,
            InputFile.FromStream(new MemoryStream("a,b"u8.ToArray()), "report.csv"),
            caption: "Draft"
        );

        // Act
        var edited = await client.EditMessageCaption(Chat, sent.Id, "Final");

        // Assert
        edited.Caption.Should().Be("Final");
    }

    [Fact]
    public async Task GetFile_OfAFileSentByUrl_ShouldSayTheFakeNeverFetchedIt()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var sent = await client.SendPhoto(Chat, InputFile.FromUri("https://example.com/cat.jpg"));

        // Act
        var act = () => client.GetFile(sent.Photo![^1].FileId);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("https://example.com/cat.jpg");
    }

    [Fact]
    public async Task SetWebhook_ShouldShowInTheWebhookInfoUntilDeleted()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.SetWebhook("https://bot.example.com/updates");
        var set = await client.GetWebhookInfo();

        // Act
        await client.DeleteWebhook();

        // Assert
        using (new AssertionScope())
        {
            set.Url.Should().Be("https://bot.example.com/updates");
            api.WebhookUrl.Should().BeNull();
            (await client.GetWebhookInfo()).Url.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("http://bot.example.com/updates", "An HTTPS URL must be provided")]
    [InlineData("https://bot.example.com:5001/updates", "only on ports 80, 88, 443 or 8443")]
    public async Task SetWebhook_WhereTelegramCannotDeliver_ShouldFailLikeTelegram(string url, string error)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var act = () => client.SetWebhook(url);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain(error);
        api.WebhookUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("https://bot.example.com:80/updates")]
    [InlineData("https://bot.example.com:88/updates")]
    [InlineData("https://bot.example.com/updates")]
    [InlineData("https://bot.example.com:8443/updates")]
    public async Task SetWebhook_OnAPortTelegramDeliversTo_ShouldBeAccepted(string url)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SetWebhook(url);

        // Assert
        api.WebhookUrl.Should().Be(url);
    }

    public static TheoryData<string, string> SecretTokensTelegramRefuses =>
        new()
        {
            { "my secret!", "Bad Request: secret token contains illegal characters" },
            { "line\nbreak", "Bad Request: secret token contains illegal characters" },
            { new string('a', 257), "Bad Request: secret token is too long" },
        };

    [Theory]
    [MemberData(nameof(SecretTokensTelegramRefuses))]
    public async Task SetWebhook_WithASecretTokenTelegramWouldRefuse_ShouldFailLikeTelegram(
        string secretToken,
        string error
    )
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        var act = () => client.SetWebhook("https://bot.example.com/updates", secretToken: secretToken);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be(error);
        api.WebhookUrl.Should().BeNull();
    }

    [Fact]
    public async Task SetWebhook_WithAnEmptySecretToken_ShouldSetNoSecret()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SetWebhook("https://bot.example.com/updates", secretToken: "");

        // Assert
        using (new AssertionScope())
        {
            api.WebhookUrl.Should().Be("https://bot.example.com/updates");
            api.Webhook()!.Value.SecretToken.Should().BeNull();
        }
    }

    [Fact]
    public async Task SetWebhook_WithTheLongestSecretToken_ShouldBeAccepted()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SetWebhook("https://bot.example.com/updates", secretToken: new string('a', 255) + "_");

        // Assert
        api.WebhookUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task SetWebhook_WithAnEmptyUrl_ShouldRemoveTheWebhook()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.SetWebhook("https://bot.example.com/updates");

        // Act
        await client.SetWebhook("");

        // Assert
        api.WebhookUrl.Should().BeNull();
    }

    [Fact]
    public async Task SetWebhook_WithACertificate_ShouldReadTheFormAsTelegramDoes()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        await client.SetWebhook(
            "https://bot.example.com/updates",
            certificate: InputFile.FromStream(new MemoryStream("pem"u8.ToArray()), "bot.pem"),
            allowedUpdates: [UpdateType.Message]
        );

        // Assert
        var info = await client.GetWebhookInfo();
        using (new AssertionScope())
        {
            info.HasCustomCertificate.Should().BeTrue();
            info.AllowedUpdates.Should().Equal(UpdateType.Message);
        }
    }

    [Fact]
    public async Task SetWebhook_WithoutAllowedUpdates_ShouldKeepTheOnesSetBefore()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        await client.SetWebhook("https://bot.example.com/updates", allowedUpdates: [UpdateType.Message]);

        // Act
        await client.SetWebhook("https://bot.example.com/other");

        // Assert
        (await client.GetWebhookInfo())
            .AllowedUpdates.Should()
            .Equal(UpdateType.Message);
    }

    [Fact]
    public async Task GetUpdates_WhileAWebhookIsSet_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        await client.SetWebhook("https://bot.example.com/updates");

        // Act
        var act = () => client.GetUpdates();

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(409);
    }

    [Fact]
    public async Task AllowedUpdates_WhenAPollOmitsThem_ShouldKeepTheOnesAskedForBefore()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.GetUpdates(allowedUpdates: [UpdateType.Message]);
        await client.GetUpdates();

        // Act
        var act = () => api.ThrowIfNotAllowed("callback_query");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Telegram would not send this callback_query to the bot: it asked only for message "
                    + "(allowed_updates). Add UpdateType.CallbackQuery to AllowedUpdates."
            );
    }

    [Fact]
    public async Task AllowedUpdates_WhenAPollAsksForNone_ShouldMeanTelegramsDefault()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.GetUpdates(allowedUpdates: [UpdateType.Message]);
        await client.GetUpdates(allowedUpdates: []);

        // Act
        var tap = () => api.ThrowIfNotAllowed("callback_query");
        var join = () => api.ThrowIfNotAllowed("chat_member");

        // Assert
        using (new AssertionScope())
        {
            tap.Should().NotThrow();
            join.Should()
                .Throw<InvalidOperationException>()
                .WithMessage(
                    "Telegram sends chat_member only to a bot that asks for it: add UpdateType.ChatMember to "
                        + "AllowedUpdates."
                );
        }
    }

    [Fact]
    public async Task GetUpdates_ThenSetWebhookWithoutAList_ShouldKeepTheListInTheWebhookInfo()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        await client.GetUpdates(allowedUpdates: [UpdateType.Message]);

        // Act
        await client.SetWebhook("https://bot.example.com/updates");

        // Assert
        (await client.GetWebhookInfo())
            .AllowedUpdates.Should()
            .Equal(UpdateType.Message);
    }

    [Fact]
    public async Task DeleteWebhook_ShouldKeepTheList()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.SetWebhook("https://bot.example.com/updates", allowedUpdates: [UpdateType.CallbackQuery]);

        // Act
        await client.DeleteWebhook();

        // Assert
        using (new AssertionScope())
        {
            (await client.GetWebhookInfo()).AllowedUpdates.Should().Equal(UpdateType.CallbackQuery);
            ((Action)(() => api.ThrowIfNotAllowed("message")))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*it asked only for callback_query*");
        }
    }

    [Fact]
    public async Task GetUpdates_RefusedWhileAWebhookIsSet_ShouldNotChangeTheList()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        await client.SetWebhook("https://bot.example.com/updates", allowedUpdates: [UpdateType.Message]);

        // Act
        await Record.ExceptionAsync(() => client.GetUpdates(allowedUpdates: [UpdateType.CallbackQuery]));

        // Assert
        (await client.GetWebhookInfo())
            .AllowedUpdates.Should()
            .Equal(UpdateType.Message);
    }

    [Fact]
    public async Task GetWebhookInfo_WithTelegramsDefault_ShouldReportNoList()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        await client.GetUpdates(allowedUpdates: [UpdateType.Message]);

        // Act
        await client.GetUpdates(allowedUpdates: []);

        // Assert
        (await client.GetWebhookInfo())
            .AllowedUpdates.Should()
            .BeNull();
    }

    [Fact]
    public async Task SetWebhook_WithOnlyUnknownTypes_ShouldGoBackToTheDefault()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        await client.GetUpdates(allowedUpdates: [UpdateType.Message]);

        // Act
        await client.SendRequest(new RawSetWebhookRequest("https://bot.example.com/updates", ["no_such_type"]));

        // Assert
        using (new AssertionScope())
        {
            (await client.GetWebhookInfo()).AllowedUpdates.Should().BeNull();
            ((Action)(() => api.ThrowIfNotAllowed("callback_query"))).Should().NotThrow();
        }
    }

    [Fact]
    public async Task SetWebhook_WithTypesInCapitals_ShouldReadThemInLowercase()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        await client.SendRequest(
            new RawSetWebhookRequest("https://bot.example.com/updates", ["CALLBACK_QUERY", "no_such_type"])
        );

        // Assert
        (await client.GetWebhookInfo())
            .AllowedUpdates.Should()
            .Equal(UpdateType.CallbackQuery);
    }

    [Fact]
    public async Task GetMe_ShouldAnswerWithTheBot()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

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
        var client = ApiWithChats().CreateClient();

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

    [Fact]
    public async Task SendMessage_ToAChatTelegramNeverSaw_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();

        // Act
        var act = () => client.SendMessage(Chat, "hello");

        // Assert
        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        using (new AssertionScope())
        {
            failure.Which.ErrorCode.Should().Be(400);
            failure.Which.Message.Should().Be("Bad Request: chat not found");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendMessage_ToAGroupMemberWhoNeverStartedTheBot_ShouldFailLikeTelegram()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();
        var anna = api.Person("Anna");
        api.Group("Family");

        // Act
        var act = () => client.SendMessage(anna["id"]!.GetValue<long>(), "hello");

        // Assert
        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        using (new AssertionScope())
        {
            failure.Which.ErrorCode.Should().Be(403);
            failure.Which.Message.Should().Be("Forbidden: bot can't initiate conversation with a user");
        }
    }

    [Fact]
    public async Task SendChatAction_ShouldBeAnsweredAndRecorded()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();

        // Act
        await client.SendChatAction(Chat, ChatAction.UploadDocument);

        // Assert
        using (new AssertionScope())
        {
            api.Calls.Should().ContainSingle(x => x.Method == "sendChatAction").Which.Parameters["action"]!
                .GetValue<string>()
                .Should()
                .Be("upload_document");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendChatAction_WithAnActionTelegramDoesNotHave_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();

        // Act
        var act = () => client.SendRequest(new RawChatActionRequest(Chat, "dancing"));

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: wrong parameter action in request");
    }

    // A fake the bot may write to Chat and 7 on, as if both users had started it.
    private static FakeBotApi ApiWithChats()
    {
        var api = new FakeBotApi();
        api.PrivateChatWith(new JsonObject { ["id"] = Chat, ["first_name"] = "Nick" });
        api.PrivateChatWith(new JsonObject { ["id"] = 7L, ["first_name"] = "Anna" });
        return api;
    }

    // An action Telegram.Bot's ChatAction cannot name.
    private sealed class RawChatActionRequest(long chatId, string action) : RequestBase<bool>("sendChatAction")
    {
        public override HttpContent ToHttpContent() => JsonContent.Create(new { chat_id = chatId, action });
    }

    // Update types Telegram.Bot's UpdateType cannot name.
    private sealed class RawSetWebhookRequest(string url, string[] allowedUpdates) : RequestBase<bool>("setWebhook")
    {
        public override HttpContent ToHttpContent() =>
            JsonContent.Create(new { url, allowed_updates = allowedUpdates });
    }

    private static string StoreDocument(FakeBotApi api, byte[] content, string fileName) =>
        api.StoreFile(FileKind.Document, content, new JsonObject { ["file_name"] = fileName })["document"]![
            "file_id"
        ]!.GetValue<string>();
}
