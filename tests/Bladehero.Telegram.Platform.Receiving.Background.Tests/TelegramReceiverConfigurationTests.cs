namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class TelegramReceiverConfigurationTests
{
    [Fact]
    public void WithoutAllowedUpdatesPollingAsksForTelegramsDefault()
    {
        var options = new TelegramReceiverConfiguration { Token = "unused" }.ToOptions();

        Assert.NotNull(options.AllowedUpdates);
        Assert.Empty(options.AllowedUpdates);
    }
}
