using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests.Webhook;

public sealed class TelegramWebhookInitializerTests
{
    private const string Url = "https://bot.example.com/telegram/updates";

    [Fact]
    public async Task WithoutAllowedUpdatesTheWebhookIsSetWithTelegramsDefault()
    {
        var client = new FakeBotClient();

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.NotNull(client.SetWebhook?.AllowedUpdates);
        Assert.Empty(client.SetWebhook.AllowedUpdates);
    }

    [Fact]
    public async Task AWebhookThatStillReportsAnOldListIsSetAgainWhenAllowedUpdatesIsUnset()
    {
        var client = new FakeBotClient(Url, webhookAllowedUpdates: [UpdateType.Message]);

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getMe", "getWebhookInfo", "deleteWebhook", "setWebhook"], client.Requests);
        Assert.Empty(client.SetWebhook!.AllowedUpdates!);
    }

    [Fact]
    public async Task TheSecretTokenIsSentWithTheWebhook()
    {
        var client = new FakeBotClient();

        await InitializerFor(client, secretToken: "s3cret").StartingAsync(CancellationToken.None);

        Assert.Equal("s3cret", client.SetWebhook?.SecretToken);
    }

    [Fact]
    public async Task WithASecretTokenTheWebhookIsSetOnEveryStart()
    {
        var client = new FakeBotClient(Url);

        await InitializerFor(client, secretToken: "s3cret").StartingAsync(CancellationToken.None);

        Assert.Equal(["getMe", "getWebhookInfo", "setWebhook"], client.Requests);
    }

    [Theory]
    [InlineData(257, 'a')]
    [InlineData(10, '!')]
    [InlineData(10, ' ')]
    public async Task AnInvalidSecretTokenFailsTheStart(int length, char character)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddTelegramWebhookReceiving(
            webhook =>
            {
                webhook.Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
                webhook.BaseUrl = "https://bot.example.com";
                webhook.UpdateEndpoint = "telegram/updates";
                webhook.SecretToken = new string('a', length - 1) + character;
            },
            typeof(ProbeCommand).Assembly
        );
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("SecretToken must be 1-256 characters of A-Z, a-z, 0-9, _ and -", failure.Message);
    }

    [Fact]
    public void ARelativeBaseUrlFailsValidation()
    {
        var options = OptionsWith(baseUrl: "bot.example.com");

        var failure = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Equal(
            "BaseUrl must be an absolute http or https URL, such as https://bot.example.com.",
            failure.Message
        );
    }

    [Fact]
    public void AnFtpBaseUrlFailsValidation()
    {
        var options = OptionsWith(baseUrl: "ftp://bot.example.com");

        var failure = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Equal(
            "BaseUrl must be an absolute http or https URL, such as https://bot.example.com.",
            failure.Message
        );
    }

    [Fact]
    public void AnHttpsBaseUrlWithAPathPassesValidation()
    {
        var options = OptionsWith(baseUrl: "https://example.com/bot/");

        Assert.Equal(new Uri("https://example.com/bot/telegram/updates"), options.Value.WebhookUri);
    }

    private static IOptions<TelegramWebhookConfiguration> OptionsWith(string baseUrl)
    {
        var services = new ServiceCollection();
        services.AddTelegramWebhookReceiving(
            webhook =>
            {
                webhook.Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
                webhook.BaseUrl = baseUrl;
                webhook.UpdateEndpoint = "telegram/updates";
            },
            typeof(ProbeCommand).Assembly
        );

        return services.BuildServiceProvider().GetRequiredService<IOptions<TelegramWebhookConfiguration>>();
    }

    private static TelegramWebhookInitializer InitializerFor(ITelegramBotClient client, string? secretToken = null) =>
        new(
            client,
            Options.Create(
                new TelegramWebhookConfiguration
                {
                    Token = "unused",
                    BaseUrl = "https://bot.example.com",
                    UpdateEndpoint = "telegram/updates",
                    SecretToken = secretToken,
                }
            ),
            NullLogger<TelegramWebhookInitializer>.Instance,
            new ClientIdentity(client)
        );
}
