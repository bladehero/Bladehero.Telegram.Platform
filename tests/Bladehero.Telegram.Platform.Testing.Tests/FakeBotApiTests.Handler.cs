using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    private const string TelegramUrl = "https://api.telegram.org";
    private const string AppKey = "1234567:nutrition_0a1b2c";

    [Fact]
    public async Task CreateHandler_ShouldAnswerAndRecordLikeAClient()
    {
        // Arrange
        var api = ApiWithChats();
        using var http = new HttpClient(api.CreateHandler());

        // Act
        var response = await http.PostAsJsonAsync(
            $"{TelegramUrl}/bot{FakeBotApi.Token}/sendMessage",
            new { chat_id = Chat, text = "through the handler" }
        );

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            api.Calls.Should().ContainSingle().Which.Parameters["text"]!
                .GetValue<string>()
                .Should()
                .Be("through the handler");
        }
    }

    [Fact]
    public async Task CreateHandler_ShouldFailWhenAsked()
    {
        // Arrange
        var api = ApiWithChats();
        api.Fail("sendMessage", new BotApiError(400, "Bad Request: message thread not found"));
        using var http = new HttpClient(api.CreateHandler());

        // Act
        var response = await http.PostAsJsonAsync(
            $"{TelegramUrl}/bot{FakeBotApi.Token}/sendMessage",
            new { chat_id = Chat, text = "refused" }
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnotherToken_ShouldBeUnauthorizedAndNotCount()
    {
        // Arrange: what a service in front of Telegram would send if it forgot to swap an app's key for the token.
        var api = ApiWithChats();
        using var http = new HttpClient(api.CreateHandler());

        // Act
        var response = await http.PostAsJsonAsync(
            $"{TelegramUrl}/bot1234567:nutrition_0a1b2c/sendMessage",
            new { chat_id = Chat, text = "with a key" }
        );

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            api.Calls.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CreateHandler_ShouldReadAUrlEncodedForm()
    {
        // Arrange
        var api = ApiWithChats();
        using var http = new HttpClient(api.CreateHandler());
        using var form = new FormUrlEncodedContent(
            new Dictionary<string, string> { ["chat_id"] = Chat.ToString(), ["text"] = "a form" }
        );

        // Act
        var response = await http.PostAsync($"{TelegramUrl}/bot{FakeBotApi.Token}/sendMessage", form);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            api.Calls.Should().ContainSingle().Which.Parameters["text"]!.GetValue<string>().Should().Be("a form");
        }
    }

    [Fact]
    public async Task ClientOnAnotherAddress_ThroughARelay_ShouldSendAndDownload()
    {
        // Arrange: an app with a key of its own, pointed at a service that swaps the key and passes everything on.
        var api = ApiWithChats();
        var voice = api.StoreUserVoice("voice"u8.ToArray());
        var relay = new Relay(api.CreateHandler());
        await using var provider = ServicesBehind(relay);
        var client = provider.GetRequiredService<ITelegramBotClient>();
        using var content = new MemoryStream();

        // Act
        var sent = await client.SendMessage(Chat, "through the relay");
        await client.GetInfoAndDownloadFile(voice.FileId, content);

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("through the relay");
            content.ToArray().Should().Equal("voice"u8.ToArray());
            relay.Seen.Should().OnlyContain(url => url.Authority == "hub:8080" && url.AbsolutePath.Contains(AppKey));
        }
    }

    [Fact]
    public async Task ClientOnAnotherAddress_ThroughARelay_ShouldUploadWithItsThreadAndName()
    {
        // Arrange
        var api = ApiWithChats();
        await using var provider = ServicesBehind(new Relay(api.CreateHandler()));
        var client = provider.GetRequiredService<ITelegramBotClient>();
        using var file = new MemoryStream("a,b"u8.ToArray());
        using var downloaded = new MemoryStream();

        // Act
        var sent = await client.SendDocument(Chat, InputFile.FromStream(file, "отчёт.csv"), messageThreadId: 5);
        await client.GetInfoAndDownloadFile(sent.Document!.FileId, downloaded);

        // Assert
        using (new AssertionScope())
        {
            sent.Document.FileName.Should().Be("отчёт.csv");
            downloaded.ToArray().Should().Equal("a,b"u8.ToArray());
            api.Calls.Should().Contain(call => call.Method == "sendDocument").Which.Parameters["message_thread_id"]!
                .ToString()
                .Should()
                .Be("5");
        }
    }

    [Fact]
    public async Task ClientOnAnotherAddress_WithoutARelay_ShouldBeRefusedByTelegram()
    {
        // Arrange: nothing swaps the key, so Telegram doesn't know it.
        var api = ApiWithChats();
        var services = new ServiceCollection();
        services.AddTelegramBot(
            x =>
            {
                x.Token = AppKey;
                x.ApiBaseUrl = "http://hub:8080";
            },
            _ => new HttpClient(api.CreateHandler())
        );
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITelegramBotClient>();

        // Act
        var send = () => client.SendMessage(Chat, "with a key");

        // Assert
        (await send.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(401);
    }

    private static ServiceProvider ServicesBehind(Relay relay)
    {
        var services = new ServiceCollection();
        services.AddTelegramBot(
            x =>
            {
                x.Token = AppKey;
                x.ApiBaseUrl = "http://hub:8080";
            },
            _ => new HttpClient(relay)
        );
        return services.BuildServiceProvider();
    }

    // Swaps the key for the token and passes on the bytes it got, not the form object the client built.
    private sealed class Relay(HttpMessageHandler telegram) : DelegatingHandler(telegram)
    {
        public List<Uri> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Seen.Add(request.RequestUri!);
            request.RequestUri = new Uri(
                TelegramUrl
                    + request.RequestUri!.PathAndQuery.Replace(AppKey, FakeBotApi.Token, StringComparison.Ordinal)
            );

            if (request.Content is { } content)
            {
                var bytes = new ByteArrayContent(await content.ReadAsByteArrayAsync(cancellationToken));
                bytes.Headers.ContentType = content.Headers.ContentType;
                request.Content = bytes;
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
