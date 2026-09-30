using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Theory]
    [InlineData("SENDMESSAGE")]
    [InlineData("sendmessage")]
    [InlineData("SendMessage")]
    public async Task Methods_ShouldMatchIgnoringCase(string method)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var body = new JsonObject { ["chat_id"] = Chat, ["text"] = "hello" };

        // Act
        var sent = await client.SendRequest(new RawRequest<Message>(method, body));
        var updates = await client.SendRequest(new RawRequest<Update[]>("GETUPDATES", new JsonObject()));

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("hello");
            updates.Should().BeEmpty();
            api.Calls.Should().ContainSingle().Which.Method.Should().Be(method);
        }
    }
}
