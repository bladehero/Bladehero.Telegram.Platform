using System.Net;
using System.Text;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

// Receivers have settings of their own, which the bot's are copied from: the address must come along with the token.
public sealed class ApiBaseUrlRegistrationTests
{
    private const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Fact]
    public async Task LongPollingPassesTheAddressToTheClient()
    {
        var requests = new List<Uri>();
        var configuration = Configuration(
            ("TelegramReceiverConfiguration:Token", Token),
            ("TelegramReceiverConfiguration:ApiBaseUrl", "http://hub:8080")
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramLongPollingReceiving(
            configuration,
            httpClientFactory: _ => new HttpClient(new Recording(requests)),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITelegramBotClient>().GetMe();

        Assert.Equal(new Uri($"http://hub:8080/bot{Token}/getMe"), Assert.Single(requests));
    }

    [Fact]
    public async Task WebhookPassesTheAddressToTheClient()
    {
        var requests = new List<Uri>();
        var configuration = Configuration(
            ("TelegramWebhookConfiguration:Token", Token),
            ("TelegramWebhookConfiguration:BaseUrl", "https://app.example.com"),
            ("TelegramWebhookConfiguration:UpdateEndpoint", "telegram/updates"),
            ("TelegramWebhookConfiguration:ApiBaseUrl", "http://hub:8080")
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramWebhookReceiving(
            configuration,
            httpClientFactory: _ => new HttpClient(new Recording(requests)),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITelegramBotClient>().GetMe();

        Assert.Equal(new Uri($"http://hub:8080/bot{Token}/getMe"), Assert.Single(requests));
    }

    [Fact]
    public async Task LongPollingKeepsTheBotsAddressWhenItHasNone()
    {
        var requests = new List<Uri>();
        var services = BotOnTheHub(requests);
        services.AddTelegramLongPollingReceiving(
            Configuration(("TelegramReceiverConfiguration:Token", Token)),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITelegramBotClient>().GetMe();

        Assert.Equal("hub", Assert.Single(requests).Host);
    }

    [Fact]
    public async Task WebhookKeepsTheBotsAddressWhenItHasNone()
    {
        var requests = new List<Uri>();
        var services = BotOnTheHub(requests);
        services.AddTelegramWebhookReceiving(
            Configuration(
                ("TelegramWebhookConfiguration:Token", Token),
                ("TelegramWebhookConfiguration:BaseUrl", "https://app.example.com"),
                ("TelegramWebhookConfiguration:UpdateEndpoint", "telegram/updates")
            ),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITelegramBotClient>().GetMe();

        Assert.Equal("hub", Assert.Single(requests).Host);
    }

    [Fact]
    public async Task LongPollingRefusesAWrongAddressOnStart()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramLongPollingReceiving(
            Configuration(
                ("TelegramReceiverConfiguration:Token", Token),
                ("TelegramReceiverConfiguration:ApiBaseUrl", "hub:8080")
            ),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetServices<IHostedService>().ToList());

        Assert.Equal(TelegramBotConfiguration.ApiBaseUrlRule, error.Message);
    }

    [Fact]
    public async Task WebhookRefusesAWrongAddressOnStart()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramWebhookReceiving(
            Configuration(
                ("TelegramWebhookConfiguration:Token", Token),
                ("TelegramWebhookConfiguration:BaseUrl", "https://app.example.com"),
                ("TelegramWebhookConfiguration:UpdateEndpoint", "telegram/updates"),
                ("TelegramWebhookConfiguration:ApiBaseUrl", "hub:8080")
            ),
            assemblies: typeof(ApiBaseUrlRegistrationTests).Assembly
        );
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetServices<IHostedService>().ToList());

        Assert.Equal(TelegramBotConfiguration.ApiBaseUrlRule, error.Message);
    }

    private static ServiceCollection BotOnTheHub(List<Uri> requests)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTelegramBot(
            x =>
            {
                x.Token = Token;
                x.ApiBaseUrl = "http://hub:8080";
            },
            _ => new HttpClient(new Recording(requests))
        );
        return services;
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

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
