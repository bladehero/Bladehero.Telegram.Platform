using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
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
    public async Task AnswerCallbackQuery_ForAQueryTelegramNeverSent_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
    public void Fail_OnGetUpdates_ShouldSayTheHostOwnsIt()
    {
        // Arrange
        var api = new FakeBotApi();

        // Act
        var act = () => api.Fail("getUpdates", BotApiError.ChatNotFound);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*test host*");
    }

    [Fact]
    public async Task DownloadFile_ShouldServeTheBytesTheUserSent()
    {
        // Arrange
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
        var client = new FakeBotApi().CreateClient();

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
        var api = new FakeBotApi();
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
        var api = new FakeBotApi();
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
            api.File(sent.Photo![^1].FileId).Content.Should().Equal("jpeg"u8.ToArray());
        }
    }

    [Fact]
    public async Task SendPhoto_WithAnEmptyUpload_ShouldFailLikeTelegram()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();

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
        var client = new FakeBotApi().CreateClient();

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
        var api = new FakeBotApi();
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
        var client = new FakeBotApi().CreateClient();
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
        var client = new FakeBotApi().CreateClient();
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
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendPhoto(
            Chat,
            InputFile.FromStream(new MemoryStream("jpeg"u8.ToArray())),
            caption: "A cat"
        );

        // Act
        var edited = await client.EditMessageCaption(Chat, sent.Id, caption: null);

        // Assert
        edited.Caption.Should().BeNull();
    }

    [Fact]
    public async Task GetFile_OfAFileSentByUrl_ShouldSayTheFakeNeverFetchedIt()
    {
        // Arrange
        var client = new FakeBotApi().CreateClient();
        var sent = await client.SendPhoto(Chat, InputFile.FromUri("https://example.com/cat.jpg"));

        // Act
        var act = () => client.GetFile(sent.Photo![^1].FileId);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("https://example.com/cat.jpg");
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

    private static string StoreDocument(FakeBotApi api, byte[] content, string fileName) =>
        api.StoreFile(FileKind.Document, content, new JsonObject { ["file_name"] = fileName })["document"]![
            "file_id"
        ]!.GetValue<string>();
}
