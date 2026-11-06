using Bladehero.Telegram.Platform.Hub;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TelegramHubTests
{
    private const string Key = "1234567:nutrition_0a1b2c";

    [Fact]
    public async Task Start_ShouldSendTheManifestAndReadThePeople()
    {
        // Arrange
        var api = new FakeBotApi();
        api.SetHubPeople((111, "Ник"), (222, "Катя"));
        await using var provider = Provider(api);

        // Act
        await StartAsync(provider);

        // Assert
        var hub = provider.GetRequiredService<ITelegramHub>();
        using (new AssertionScope())
        {
            api.HubApp!["title"]!.GetValue<string>().Should().Be("Питание");
            api.HubApp["site"]!.GetValue<string>().Should().Be("https://nutrition.example");
            api.HubApp["takes_sign_in_codes"]!.GetValue<bool>().Should().BeTrue();
            api.HubApp.ContainsKey("takes_groups").Should().BeFalse("a false value is left out");
            api.HubApp["unlisted_commands"]![0]!.GetValue<string>().Should().Be("password");
            hub.KnowsPeople.Should().BeTrue();
            hub.People.Should().HaveCount(2);
            hub.FindPerson(222)!.Name.Should().Be("Катя");
            hub.FindPerson(333).Should().BeNull();
        }
    }

    [Fact]
    public async Task HubDownAtStart_ShouldStartAnywayKnowingNobodyYet()
    {
        // Arrange
        var api = new FakeBotApi();
        api.Fail("setHubApp", new BotApiError(502, "Bad Gateway"), times: int.MaxValue);
        await using var provider = Provider(api, x => x.StartWait = TimeSpan.FromMilliseconds(200));

        // Act
        await StartAsync(provider);

        // Assert
        var hub = provider.GetRequiredService<ITelegramHub>();
        using (new AssertionScope())
        {
            hub.KnowsPeople.Should().BeFalse();
            hub.People.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ThePersonTakenOff_ShouldBeGoneAtTheNextRefresh()
    {
        // Arrange
        var api = new FakeBotApi();
        api.SetHubPeople((111, "Ник"), (222, "Катя"));
        await using var provider = Provider(api, x => x.RefreshEvery = TimeSpan.FromMilliseconds(20));
        await StartAsync(provider);
        var hub = provider.GetRequiredService<ITelegramHub>();

        // Act
        api.SetHubPeople((111, "Ник"));

        // Assert
        await WaitUntilAsync(() => hub.People.Count == 1);
        hub.FindPerson(222).Should().BeNull();
    }

    [Fact]
    public async Task HubDownLater_ShouldKeepTheLastCopy()
    {
        // Arrange
        var api = new FakeBotApi();
        api.SetHubPeople((111, "Ник"));
        await using var provider = Provider(api, x => x.RefreshEvery = TimeSpan.FromMilliseconds(20));
        await StartAsync(provider);
        var mark = api.Calls.Count;

        // Act
        api.Fail("setHubApp", new BotApiError(502, "Bad Gateway"), times: int.MaxValue);
        await WaitUntilAsync(() => api.Calls.Count > mark + 2);

        // Assert
        provider.GetRequiredService<ITelegramHub>().FindPerson(111).Should().NotBeNull();
    }

    [Fact]
    public async Task SignInLink_WithAKey_ShouldCarryTheAppsPrefix()
    {
        // Arrange
        await using var provider = Provider(new FakeBotApi());

        // Act
        var link = await provider.GetRequiredService<ITelegramHub>().SignInLinkAsync("abc123");

        // Assert
        link.Should().Be("https://t.me/test_bot?start=nutrition-abc123");
    }

    [Fact]
    public async Task SignInLink_WithARealToken_ShouldBePlain()
    {
        // Arrange
        await using var provider = Provider(new FakeBotApi(), token: FakeBotApi.Token);

        // Act
        var link = await provider.GetRequiredService<ITelegramHub>().SignInLinkAsync("abc123");

        // Assert
        link.Should().Be("https://t.me/test_bot?start=abc123");
    }

    [Fact]
    public void AddTelegramHub_WithoutATitle_ShouldBeRefusedOnStart()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramHub(_ => { });
        using var provider = services.BuildServiceProvider();

        // Act
        var read = () =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramHubConfiguration>>().Value;

        // Assert
        read.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    [Fact]
    public async Task AHubThatMissedTheWebhook_ShouldHearItAtTheNextRefresh_WithNothingDropped()
    {
        // Arrange
        var api = new FakeBotApi();
        api.Fail("getWebhookInfo", new BotApiError(502, "Bad Gateway"), times: 1);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TelegramWebhookConfiguration:Token"] = FakeBotApi.Token,
                    ["TelegramWebhookConfiguration:BaseUrl"] = "https://nutrition.example",
                    ["TelegramWebhookConfiguration:UpdateEndpoint"] = "telegram/updates",
                    ["TelegramWebhookConfiguration:DropPendingUpdates"] = "true",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramWebhookReceiving(
            configuration,
            httpClientFactory: _ => new HttpClient(api.CreateHandler()),
            assemblies: typeof(TelegramHubTests).Assembly
        );
        services.AddTelegramHub(x =>
        {
            x.Title = "Питание";
            x.RefreshEvery = TimeSpan.FromMilliseconds(20);
        });
        await using var provider = services.BuildServiceProvider();
        var mark = api.Mark();

        // Act
        await StartAsync(provider);

        // Assert
        var call = await api.WaitForCallAsync("setWebhook", after: mark);
        using (new AssertionScope())
        {
            call.Parameters["url"]!.GetValue<string>().Should().Be("https://nutrition.example/telegram/updates");
            (call.Parameters["drop_pending_updates"]?.GetValue<bool>() ?? false).Should().BeFalse();
        }
    }

    private static ServiceProvider Provider(
        FakeBotApi api,
        Action<TelegramHubConfiguration>? configure = null,
        string token = Key
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ITelegramBotClient>(api.CreateClient());
        services.AddTelegramBot(x => x.Token = token);
        services.AddTelegramHub(x =>
        {
            x.Title = "Питание";
            x.Site = "https://nutrition.example";
            x.TakesSignInCodes = true;
            x.UnlistedCommands = ["password"];
            configure?.Invoke(x);
        });
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(ServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(CancellationToken.None);
            await service.StartedAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(10);
        }

        done().Should().BeTrue();
    }
}
