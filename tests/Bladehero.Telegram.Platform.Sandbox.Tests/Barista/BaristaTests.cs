using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Barista;

// The barista's clock is a FakeTimeProvider, so a coffee brews the moment the test moves it on.
public sealed class BaristaTests
{
    [Fact]
    public async Task Order_ShouldBeAnnouncedOnceBrewed()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var bot = await SandboxBot.StartAsync(configure: SandboxBot.Using<TimeProvider>(time));
        var nick = bot.PrivateChat("Nick");
        await OrderAsync(nick, "Nicky");
        var announcedWhileBrewing = nick.Messages.Any(IsReady);

        // Act
        time.Advance(TimeSpan.FromMinutes(1));
        var ready = await nick.WaitForMessageAsync(IsReady);

        // Assert
        using (new AssertionScope())
        {
            announcedWhileBrewing.Should().BeFalse();
            ready.Text.Should().Be("☕ Your Medium coffee for Nicky is ready!");
        }
    }

    [Fact]
    public async Task Order_ForACustomerWhoBlockedTheBot_ShouldNotStopTheBarista()
    {
        // Arrange
        var time = new FakeTimeProvider();
        await using var bot = await SandboxBot.StartAsync(configure: SandboxBot.Using<TimeProvider>(time));
        var nick = bot.PrivateChat("Nick");
        var anna = bot.PrivateChat("Anna");
        await OrderAsync(nick, "Nicky");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked, chatId: nick.Chat.Id);
        await OrderAsync(anna, "Annie");

        // Act
        time.Advance(TimeSpan.FromMinutes(1));
        var ready = await anna.WaitForMessageAsync(IsReady);

        // Assert
        using (new AssertionScope())
        {
            ready.Text.Should().Be("☕ Your Medium coffee for Annie is ready!");
            bot.Api.Calls.Should()
                .Contain(x =>
                    x.Method == "sendMessage"
                    && x.Parameters["chat_id"]!.GetValue<long>() == nick.Chat.Id
                    && x.Parameters["text"]!.GetValue<string>() == "☕ Your Medium coffee for Nicky is ready!"
                );
        }
    }

    private static bool IsReady(TestMessage message) => message.Text?.StartsWith('☕') is true;

    private static async Task OrderAsync(TestUser customer, string cupName)
    {
        await customer.SendsAsync("/coffee medium");
        await customer.SendsAsync(cupName);
        await customer.TapsAsync("Confirm");
    }
}
