using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class TelegramLongPollingInitializerTests
{
    [Fact]
    public async Task AnActiveWebhookIsDeletedSoPollingCanStart()
    {
        var client = new FakeBotClient("https://bot.example.com/telegram/updates");

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getWebhookInfo", "deleteWebhook"], client.Requests);
    }

    [Fact]
    public async Task WithNoWebhookRegisteredNothingIsDeleted()
    {
        var client = new FakeBotClient(webhookUrl: null);

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getWebhookInfo"], client.Requests);
    }

    [Fact]
    public async Task TheDeleteCarriesTheConfiguredPendingUpdateHandling()
    {
        var client = new FakeBotClient("https://bot.example.com/telegram/updates");

        await InitializerFor(client, dropPendingUpdates: true).StartingAsync(CancellationToken.None);

        Assert.True(client.DroppedPendingUpdates);
    }

    [Fact]
    public async Task AFailureToReachTelegramDoesNotStopTheHostFromStarting()
    {
        var client = new FakeBotClient(unreachable: true);

        var exception = await Record.ExceptionAsync(() => InitializerFor(client).StartingAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    private static TelegramLongPollingInitializer InitializerFor(
        ITelegramBotClient client,
        bool dropPendingUpdates = false
    ) =>
        new(
            new TelegramBotClientAccessor(client),
            Options.Create(
                new TelegramReceiverConfiguration { Token = "unused", DropPendingUpdates = dropPendingUpdates }
            ),
            NullLogger<TelegramLongPollingInitializer>.Instance
        );
}
