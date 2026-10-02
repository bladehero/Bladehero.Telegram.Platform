using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Activity;

public sealed class RecentTests
{
    [Fact]
    public async Task Recent_AfterAnOrderStarts_ShouldListWhatHappenedInTheChat()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");

        // Act
        await nick.SendsAsync("/recent");

        // Assert
        nick.LastMessage.Text.Should()
            .Be(
                """
                Lately in this chat:
                message: /coffee
                sendMessage: What size?
                message: /recent
                """.ReplaceLineEndings("\n")
            );
    }

    [Fact]
    public async Task History_OfASizeTap_ShouldLinkTheAnswerTheEditAndTheNextPromptToTheTap()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee");

        // Act
        await nick.TapsAsync("Large");

        // Assert
        var tap = (await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id })).Last(x => x.Kind == "callback_query");
        var caused = await bot.History.ReadAsync(new() { UpdateId = tap.UpdateId });
        using (new AssertionScope())
        {
            caused
                .Select(x => x.Kind)
                .Should()
                .Equal("callback_query", "answerCallbackQuery", "editMessageText", "sendMessage");
            caused.Should().OnlyContain(x => x.ChatId == nick.Chat.Id);
        }
    }

    [Fact]
    public async Task History_OfTheBaristasReadyMessage_ShouldHaveNoUpdate()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var bot = await SandboxBot.StartAsync(configure: SandboxBot.Using<TimeProvider>(time));
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee medium");
        await nick.SendsAsync("Nicky");
        await nick.TapsAsync("Confirm");

        // Act
        time.Advance(TimeSpan.FromMinutes(1));
        await nick.WaitForMessageAsync(x => x.Text?.StartsWith('☕') is true);

        // Assert
        (await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id }))
            .Should()
            .ContainSingle(x => x.Text == "☕ Your Medium coffee for Nicky is ready!")
            .Which.Should()
            .BeEquivalentTo(new { Kind = "sendMessage", UpdateId = (int?)null });
    }

    [Fact]
    public async Task History_OfAnEditedCupName_ShouldShowBothVersionsOfTheMessage()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/coffee medium");
        var name = await nick.SendsAsync("Nicky");

        // Act
        await nick.EditsAsync(name, "Nicolas");

        // Assert
        (await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id, MessageId = name.Id }))
            .Select(x => x.ToString())
            .Should()
            .Equal("message: Nicky", "edited_message: Nicolas");
    }
}
