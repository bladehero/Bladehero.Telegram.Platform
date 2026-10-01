using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task UserIdOf_WithDetails_ShouldPutThemOnTheUserAndThePrivateChat()
    {
        // Arrange
        var api = new FakeBotApi();
        var id = api.UserIdOf("Nick", "Doe", "nick_d", "uk");
        await using var bot = await TestBot.StartAsync(api);
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await api.CreateClient().SendMessage(id, "hello");
        await nick.SendsAsync("/details");

        // Assert
        using (new AssertionScope())
        {
            (sent.Chat.FirstName, sent.Chat.LastName, sent.Chat.Username).Should().Be(("Nick", "Doe", "nick_d"));
            nick.LastMessage.Text.Should().Be("Nick Doe @nick_d uk; chat @nick_d");
        }
    }
}
