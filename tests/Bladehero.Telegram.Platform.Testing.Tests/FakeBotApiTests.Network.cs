using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    private const string TooOld = "Bad Request: query is too old and response timeout expired or query ID is invalid";

    [Fact]
    public async Task TimeOut_ShouldFailTheCallAsATimeoutAndChangeNothing()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.TimeOut("sendMessage");

        // Act
        var failure = await Record.ExceptionAsync(() => client.SendMessage(Chat, "Continue?"));

        // Assert
        using (new AssertionScope())
        {
            failure.Should().BeOfType<RequestException>().Which.Message.Should().Be("Bot API Request timed out");
            failure!.InnerException.Should().BeOfType<TaskCanceledException>();
            api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TimeOut_ShouldNotLookLikeAShutdownToTheBot()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        bot.Api.TimeOut("sendMessage", times: 1);

        // Act
        var failure = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));
        await nick.SendsAsync("still here");

        // Assert
        using (new AssertionScope())
        {
            failure.Should().BeOfType<RequestException>().Which.Message.Should().Be("Bot API Request timed out");
            nick.LastMessage.ToString().Should().Be("Bot: still here");
        }
    }

    [Fact]
    public async Task LoseResponse_OnSendMessage_ShouldPostTheMessageAndThenFail()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.LoseResponse("sendMessage");

        // Act
        var failure = await Record.ExceptionAsync(() => client.SendMessage(Chat, "Continue?"));

        // Assert
        using (new AssertionScope())
        {
            failure
                .Should()
                .BeOfType<RequestException>()
                .Which.Message.Should()
                .Be(
                    "Bot API Service Failure: HttpRequestException: The response to sendMessage was lost on the way "
                        + "back, as FakeBotApi.LoseResponse asked; Telegram did it."
                );
            failure!.InnerException.Should().BeOfType<HttpRequestException>();
            api.MessagesIn(Chat).Select(x => x["text"]!.GetValue<string>()).Should().Equal("Continue?");
        }
    }

    [Fact]
    public async Task LoseResponse_ThenARetry_ShouldPostTwice()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.LoseResponse("sendMessage", times: 1);
        await Record.ExceptionAsync(() => client.SendMessage(Chat, "Paid 12 EUR"));

        // Act
        await client.SendMessage(Chat, "Paid 12 EUR");

        // Assert
        api.MessagesIn(Chat).Select(x => x["text"]!.GetValue<string>()).Should().Equal("Paid 12 EUR", "Paid 12 EUR");
    }

    [Fact]
    public async Task LoseResponse_OnAnswerCallbackQuery_ThenARetry_ShouldBeRefusedAsTooOld()
    {
        // Arrange: Telegram sent the tap "q1", so it takes one answer to it.
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Enqueue(new JsonObject { ["callback_query"] = new JsonObject { ["id"] = "q1" } });
        api.LoseResponse("answerCallbackQuery", times: 1);
        var lost = await Record.ExceptionAsync(() => client.AnswerCallbackQuery("q1", "Saved"));

        // Act
        var retry = () => client.AnswerCallbackQuery("q1", "Saved");

        // Assert
        using (new AssertionScope())
        {
            lost.Should().BeOfType<RequestException>();
            (await retry.Should().ThrowAsync<ApiRequestException>()).Which.Message.Should().Be(TooOld);
        }
    }

    [Theory]
    [InlineData("TimeOut", "getUpdates", null)]
    [InlineData("TimeOut", "answerCallbackQuery", 7L)]
    [InlineData("LoseResponse", "getUpdates", null)]
    [InlineData("LoseResponse", "answerCallbackQuery", 7L)]
    public void TimeOut_AndLoseResponse_WithGetUpdatesOrAChatlessChatId_ShouldThrowAsFailNetworkDoes(
        string failure,
        string method,
        long? chatId
    )
    {
        // Arrange
        var api = new FakeBotApi();
        Action<string, int?, long?> fail = failure == "TimeOut" ? api.TimeOut : api.LoseResponse;

        // Act
        var act = () => fail(method, null, chatId);
        var asFailNetwork = () => api.FailNetwork(method, null, chatId);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .Be(asFailNetwork.Should().Throw<ArgumentException>().Which.Message);
    }

    [Fact]
    public async Task FailDownload_WithAnError_ShouldRefuseTheDownload()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, "hello"u8.ToArray(), "hello.txt");
        var file = await client.GetFile(fileId);
        api.FailDownload(fileId, new BotApiError(404, "Not Found"));
        using var content = new MemoryStream();

        // Act
        var act = () => client.DownloadFile(file, content);

        // Assert
        var refused = (await act.Should().ThrowAsync<ApiRequestException>()).Which;
        using (new AssertionScope())
        {
            refused.ErrorCode.Should().Be(404);
            refused.Message.Should().Be("Not Found");
        }
    }

    [Fact]
    public async Task FailDownload_WithoutAnError_ShouldBreakTheDownload()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, "hello"u8.ToArray(), "hello.txt");
        var file = await client.GetFile(fileId);
        api.FailDownload(fileId);
        using var content = new MemoryStream();

        // Act
        var act = () => client.DownloadFile(file, content);

        // Assert
        var broken = (await act.Should().ThrowAsync<RequestException>()).Which;
        using (new AssertionScope())
        {
            broken.Message.Should().Be("Exception during file download");
            broken
                .InnerException.Should()
                .BeOfType<HttpRequestException>()
                .Which.Message.Should()
                .Be($"The download of {fileId} broke off, as FakeBotApi.FailDownload asked.");
        }
    }

    [Fact]
    public async Task FailDownload_ForTimes_ShouldThenLetItThrough()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, "hello"u8.ToArray(), "hello.txt");
        var file = await client.GetFile(fileId);
        api.FailDownload(fileId, times: 1);
        await Record.ExceptionAsync(() => client.DownloadFile(file, new MemoryStream()));
        using var content = new MemoryStream();

        // Act
        await client.DownloadFile(file, content);

        // Assert
        content.ToArray().Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public void FailDownload_ForAnUnknownFile_ShouldThrow()
    {
        // Arrange
        var api = new FakeBotApi();

        // Act
        var act = () => api.FailDownload("file_404");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("fileId")
            .WithMessage("Telegram has no file file_404; it was never sent.*");
    }

    [Fact]
    public async Task FailDownloads_ShouldFailTheNextDownloadOfAnyFile()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var file = await client.GetFile(StoreDocument(api, "hello"u8.ToArray(), "hello.txt"));
        api.FailDownloads(new BotApiError(404, "Not Found"));

        // Act
        var act = () => client.DownloadFile(file, new MemoryStream());

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(404);
    }

    [Fact]
    public async Task FailDownloads_ForTimes_ShouldThenLetDownloadsThrough()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var file = await client.GetFile(StoreDocument(api, "hello"u8.ToArray(), "hello.txt"));
        api.FailDownloads(times: 1);
        await Record.ExceptionAsync(() => client.DownloadFile(file, new MemoryStream()));
        using var content = new MemoryStream();

        // Act
        await client.DownloadFile(file, content);

        // Assert
        content.ToArray().Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public async Task FailDownload_ForAFile_ShouldComeBeforeFailDownloads()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var fileId = StoreDocument(api, "hello"u8.ToArray(), "hello.txt");
        var file = await client.GetFile(fileId);
        api.FailDownloads(new BotApiError(500, "Internal Server Error"), times: 1);
        api.FailDownload(fileId, new BotApiError(404, "Not Found"), times: 1);

        // Act
        var first = await Record.ExceptionAsync(() => client.DownloadFile(file, new MemoryStream()));
        var second = await Record.ExceptionAsync(() => client.DownloadFile(file, new MemoryStream()));

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(404);
            second.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(500);
        }
    }
}
