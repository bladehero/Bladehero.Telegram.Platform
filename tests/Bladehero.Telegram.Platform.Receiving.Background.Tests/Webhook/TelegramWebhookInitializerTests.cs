using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
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

    private static TelegramWebhookInitializer InitializerFor(ITelegramBotClient client) =>
        new(
            new TelegramBotClientAccessor(client),
            Options.Create(
                new TelegramWebhookConfiguration
                {
                    Token = "unused",
                    BaseUrl = "https://bot.example.com",
                    UpdateEndpoint = "telegram/updates",
                }
            ),
            NullLogger<TelegramWebhookInitializer>.Instance
        );
}
