using FluentAssertions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class CommandMenuTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task CommandMenu_ShouldListTheSandboxCommands(BotMode mode)
    {
        // Act
        await using var bot = await SandboxBot.StartAsync(mode);

        // Assert: none sets an Order, so they are listed alphabetically.
        bot.Api.CommandMenu()
            .Select(x => $"/{x.Command} {x.Description}")
            .Should()
            .Equal(
                "/recall Recall your note",
                "/remember Remember a note",
                "/translate Translate text, e.g. /translate hello"
            );
    }
}
