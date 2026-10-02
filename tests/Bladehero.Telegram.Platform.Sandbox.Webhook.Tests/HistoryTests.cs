using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.History.InMemory;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.TestHost;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class HistoryTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task History_InEitherMode_ShouldRecordTheEchoAndLinkItsReply(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web => web.ConfigureTestServices(s => s.AddTelegramHistory().UseInMemory())
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        var entries = await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id });
        using (new AssertionScope())
        {
            entries.Select(x => x.ToString()).Should().Equal("message: hello", "sendMessage: Reply: hello");
            entries.Select(x => x.UpdateId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        }
    }
}
