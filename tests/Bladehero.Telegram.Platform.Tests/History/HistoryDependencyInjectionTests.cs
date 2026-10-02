using System.Net;
using System.Text;
using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class HistoryDependencyInjectionTests
{
    [Fact]
    public async Task AddTelegramHistory_WithoutAStore_ShouldFailToStartNamingTheChoices()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should()
            .ContainAll("UseInMemory()", "UseEntityFrameworkCore<TContext>()", "ITelegramHistoryStore");
    }

    [Fact]
    public async Task AddTelegramHistory_WithAQueueCapacityBelowOne_ShouldFailToStart()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory(x => x.QueueCapacity = 0).Services.AddSingleton(Store());
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("Telegram history QueueCapacity must be at least 1.");
    }

    [Fact]
    public void AddTelegramHistory_Twice_ShouldRunOneWriter()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Act
        services.AddTelegramHistory();

        // Assert
        using var provider = services.BuildServiceProvider();
        provider
            .GetServices<IHostedService>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TelegramHistoryWriter>();
    }

    [Fact]
    public void AddTelegramHistory_ShouldReturnABuilderOverTheSameServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var history = services.AddTelegramHistory();

        // Assert
        history.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddTelegramHistory_AfterAddTelegramBot_ShouldRecordTheLibrarysClient()
    {
        // Arrange
        var services = Services();
        services.AddTelegramBot(x => x.Token = Token);

        // Act
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Assert
        using var provider = Provider(services);
        ClientOf(provider).Should().BeOfType<RecordingBotClient>().Which.Inner.Should().BeOfType<TelegramBotClient>();
    }

    [Fact]
    public void AddTelegramHistory_BeforeAddTelegramBot_ShouldRecordTheLibrarysClient()
    {
        // Arrange
        var services = Services();
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Act
        services.AddTelegramBot(x => x.Token = Token);

        // Assert
        using var provider = Provider(services);
        ClientOf(provider).Should().BeOfType<RecordingBotClient>().Which.Inner.Should().BeOfType<TelegramBotClient>();
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("factory")]
    [InlineData("type")]
    public void AddTelegramHistory_AfterTheAppsOwnClient_ShouldRecordIt(string registration)
    {
        // Arrange
        var services = Services();
        _ = registration switch
        {
            "instance" => services.AddSingleton<ITelegramBotClient>(new OwnClient()),
            "factory" => services.AddSingleton<ITelegramBotClient>(_ => new OwnClient()),
            _ => services.AddSingleton<ITelegramBotClient, OwnClient>(),
        };
        services.AddTelegramBot(x => x.Token = Token);

        // Act
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Assert
        using var provider = Provider(services);
        ClientOf(provider).Should().BeOfType<RecordingBotClient>().Which.Inner.Should().BeOfType<OwnClient>();
    }

    [Fact]
    public async Task AddTelegramHistory_BeforeTheAppsOwnClient_ShouldFailToStartNamingTheFix()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory().Services.AddSingleton(Store());
        builder.Services.AddSingleton<ITelegramBotClient>(new OwnClient());
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage(
                "The bot's calls wouldn't be recorded: an ITelegramBotClient is registered after AddTelegramHistory(). "
                    + "Move AddTelegramHistory() below your own ITelegramBotClient registration "
                    + "(e.g. services.AddSingleton<ITelegramBotClient>(...)), or call it again after that registration, "
                    + "as a test that replaces the client must."
            );
    }

    [Fact]
    public async Task AddTelegramHistory_WithoutAStoreAndAClient_ShouldFailWithTheStoreMessage()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramBot(x => x.Token = Token);
        builder.Services.AddTelegramHistory();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("Telegram history needs a store*");
    }

    [Fact]
    public void AddTelegramHistory_ShouldLeaveKeyedClientsAlone()
    {
        // Arrange
        var services = Services();
        services.AddKeyedSingleton<ITelegramBotClient>("alerts", new OwnClient());
        services.AddTelegramBot(x => x.Token = Token);

        // Act
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Assert
        using var provider = Provider(services);
        using (new AssertionScope())
        {
            provider.GetRequiredKeyedService<ITelegramBotClient>("alerts").Should().BeOfType<OwnClient>();
            ClientOf(provider).Should().BeOfType<RecordingBotClient>();
        }
    }

    [Fact]
    public async Task AddTelegramHistory_WithoutAnyClient_ShouldStart()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory().Services.AddSingleton(Store());
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddTelegramHistory_Twice_ShouldWrapTheClientOnce()
    {
        // Arrange
        var services = Services();
        services.AddTelegramBot(x => x.Token = Token);
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Act
        services.AddTelegramHistory();

        // Assert
        using var provider = Provider(services);
        ClientOf(provider).Should().BeOfType<RecordingBotClient>().Which.Inner.Should().BeOfType<TelegramBotClient>();
    }

    [Fact]
    public async Task Messages_WithHistory_ShouldRecordTheirCalls()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(services: x =>
            x.AddTelegramBot(bot => bot.Token = Token, httpClientFactory: _ => new HttpClient(new CannedTelegram()))
        );
        var messages = host.Host.Services.GetRequiredService<ITelegramMessages>();

        // Act
        var sent = await messages.SendAsync(HistoryHost.ChatId, "Hi");
        await host.History.FlushAsync();

        // Assert
        using (new AssertionScope())
        {
            sent.Should().Be(new TelegramMessageRef(HistoryHost.ChatId, 10));
            host.Store.Entries.Should()
                .ContainSingle()
                .Which.Should()
                .BeEquivalentTo(
                    new
                    {
                        Kind = "sendMessage",
                        ChatId = HistoryHost.ChatId,
                        MessageId = 10,
                        Text = "Hi",
                    }
                );
        }
    }

    private const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    private static ITelegramHistoryStore Store() => Mock.Of<ITelegramHistoryStore>();

    private static IServiceCollection Services() => new ServiceCollection().AddLogging();

    private static ServiceProvider Provider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    private static ITelegramBotClient ClientOf(IServiceProvider provider) =>
        provider.GetRequiredService<ITelegramBotClient>();

    private sealed class OwnClient() : TelegramBotClient(HistoryDependencyInjectionTests.Token);

    // Answers every call with the message the bot sent.
    private sealed class CannedTelegram : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"ok":true,"result":{"message_id":10,"date":1790000000,"text":"Hi",
                        "chat":{"id":-1001234567890,"type":"supergroup","title":"Coffee lovers"}}}
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }
}
