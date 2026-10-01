using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task SendMessage_ToAChatThatBlockedTheBot_ShouldBeRefusedWith403()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        api.Block(Nick(), blocked: true);

        // Act
        var act = () => client.SendMessage(Chat, "Your limit is near");

        // Assert
        var refused = (await act.Should().ThrowAsync<ApiRequestException>()).Which;
        using (new AssertionScope())
        {
            refused.ErrorCode.Should().Be(403);
            refused.Message.Should().Be("Forbidden: bot was blocked by the user");
            api.MessagesIn(Chat).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task EditMessageText_InAChatThatBlockedTheBot_ShouldSucceed()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var sent = await client.SendMessage(Chat, "Your limit is near");
        api.Block(Nick(), blocked: true);

        // Act
        var edited = await client.EditMessageText(Chat, sent.Id, "Your limit is reached");

        // Assert
        edited.Text.Should().Be("Your limit is reached");
    }

    // The user of chat 42.
    private static JsonObject Nick() => new() { ["id"] = Chat, ["first_name"] = "Nick" };
}
