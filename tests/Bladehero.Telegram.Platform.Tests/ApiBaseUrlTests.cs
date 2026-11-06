using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Tests;

public sealed class ApiBaseUrlTests
{
    private const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Fact]
    public async Task WithoutIt_ShouldTalkToTelegram()
    {
        // Arrange
        var requests = new List<Uri>();
        var client = ClientWith(apiBaseUrl: null, requests);

        // Act
        await client.GetMe();

        // Assert
        requests.Should().ContainSingle().Which.Should().Be(new Uri($"https://api.telegram.org/bot{Token}/getMe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_ShouldTalkToTelegram(string apiBaseUrl)
    {
        // Arrange: an environment variable a compose file leaves empty.
        var requests = new List<Uri>();
        var client = ClientWith(apiBaseUrl, requests);

        // Act
        await client.GetMe();

        // Assert
        requests.Should().ContainSingle().Which.Host.Should().Be("api.telegram.org");
    }

    [Theory]
    [InlineData("http://hub:8080")]
    [InlineData("http://hub:8080/")]
    public async Task Set_ShouldSendRequestsThere(string apiBaseUrl)
    {
        // Arrange
        var requests = new List<Uri>();
        var client = ClientWith(apiBaseUrl, requests);

        // Act
        await client.GetMe();

        // Assert
        requests.Should().ContainSingle().Which.Should().Be(new Uri($"http://hub:8080/bot{Token}/getMe"));
    }

    [Fact]
    public async Task Set_ShouldSendFileDownloadsThere()
    {
        // Arrange
        var requests = new List<Uri>();
        var client = ClientWith("http://hub:8080", requests);
        using var content = new MemoryStream();

        // Act
        await client.DownloadFile("voice/file_1.oga", content);

        // Assert
        requests
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(new Uri($"http://hub:8080/file/bot{Token}/voice/file_1.oga"));
    }

    [Fact]
    public void Set_ShouldStillReadTheBotIdFromAKeyShapedLikeAToken()
    {
        // Arrange: not @BotFather's token, but a key a service in front of Telegram gave this app.
        var services = new ServiceCollection();
        services.AddTelegramBot(x =>
        {
            x.Token = "1234567:nutrition_0a1b2c";
            x.ApiBaseUrl = "http://hub:8080";
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var client = provider.GetRequiredService<ITelegramBotClient>();

        // Assert
        client.BotId.Should().Be(1234567);
    }

    [Theory]
    [InlineData("hub:8080")]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("ftp://hub:8080")]
    [InlineData("http://hub:8080/some/path")]
    [InlineData("http://hub:8080?a=b")]
    [InlineData("http://hub:8080#a")]
    [InlineData("http://user:password@hub:8080")]
    public void Wrong_ShouldSaySoWhenTheClientIsBuilt(string apiBaseUrl)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramBot(x =>
        {
            x.Token = Token;
            x.ApiBaseUrl = apiBaseUrl;
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var build = () => provider.GetRequiredService<ITelegramBotClient>();

        // Assert
        build
            .Should()
            .Throw<OptionsValidationException>()
            .Which.Message.Should()
            .Be(TelegramBotConfiguration.ApiBaseUrlRule);
    }

    [Fact]
    public void Wrong_WithTheBotAddedTwice_ShouldSaySoOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramBot(x => x.Token = Token);
        services.AddTelegramBot(x => x.ApiBaseUrl = "hub:8080");
        using var provider = services.BuildServiceProvider();

        // Act
        var build = () => provider.GetRequiredService<ITelegramBotClient>();

        // Assert
        build
            .Should()
            .Throw<OptionsValidationException>()
            .Which.Message.Should()
            .Be(TelegramBotConfiguration.ApiBaseUrlRule);
    }

    private static ITelegramBotClient ClientWith(string? apiBaseUrl, List<Uri> requests)
    {
        var services = new ServiceCollection();
        services.AddTelegramBot(
            x =>
            {
                x.Token = Token;
                x.ApiBaseUrl = apiBaseUrl;
            },
            _ => new HttpClient(new Recording(requests))
        );

        return services.BuildServiceProvider().GetRequiredService<ITelegramBotClient>();
    }

    private sealed class Recording(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            requests.Add(request.RequestUri!);
            const string me = """{"ok":true,"result":{"id":1234567,"is_bot":true,"first_name":"Bot"}}""";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(me, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
