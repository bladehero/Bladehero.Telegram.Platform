using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task SetMessageReaction_ShouldSetAndClearTheBotsReaction()
    {
        // Arrange: "❤" without the variation selector is the one Telegram offers.
        var api = ApiWithChats();
        var client = api.CreateClient();
        var sent = await client.SendMessage(Chat, "Paid 12 EUR");
        await client.SetMessageReaction(Chat, sent.Id, [new ReactionTypeEmoji { Emoji = "❤" }]);
        var set = api.ReactionsOn(Chat, sent.Id);

        // Act
        await client.SetMessageReaction(Chat, sent.Id, []);

        // Assert
        using (new AssertionScope())
        {
            set.Should().Equal("❤");
            api.ReactionsOn(Chat, sent.Id).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("two", 400, "Bad Request: REACTIONS_TOO_MANY")]
    [InlineData("❤️", 400, "Bad Request: REACTION_INVALID")]
    [InlineData("🙂", 400, "Bad Request: REACTION_INVALID")]
    [InlineData("custom_emoji", 404, "Not Found: FakeBotApi does not support custom_emoji and paid reactions yet")]
    [InlineData("paid", 404, "Not Found: FakeBotApi does not support custom_emoji and paid reactions yet")]
    [InlineData("sticker", 400, "Bad Request: invalid reaction type specified")]
    [InlineData("missing", 400, "Bad Request: message to react not found")]
    public async Task SetMessageReaction_ShouldBeRefusedLikeTelegram(string reaction, int errorCode, string description)
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var sent = await client.SendMessage(Chat, "Paid 12 EUR");
        JsonObject Emoji(string emoji) => new() { ["type"] = "emoji", ["emoji"] = emoji };
        var reactions = reaction switch
        {
            "two" => new JsonArray(Emoji("👍"), Emoji("🔥")),
            "custom_emoji" => new JsonArray(new JsonObject { ["type"] = "custom_emoji", ["custom_emoji_id"] = "1" }),
            "paid" or "sticker" => new JsonArray(new JsonObject { ["type"] = reaction }),
            "missing" => new JsonArray(Emoji("👍")),
            _ => new JsonArray(Emoji(reaction)),
        };
        var body = new JsonObject
        {
            ["chat_id"] = Chat,
            ["message_id"] = reaction == "missing" ? 999 : sent.Id,
            ["reaction"] = reactions,
        };

        // Act
        var act = () => client.SendRequest(new RawRequest<bool>("setMessageReaction", body));

        // Assert
        var refused = (await act.Should().ThrowAsync<ApiRequestException>()).Which;
        using (new AssertionScope())
        {
            refused.ErrorCode.Should().Be(errorCode);
            refused.Message.Should().Be(description);
        }
    }

    [Fact]
    public async Task DeleteMessages_ShouldDeleteTheFoundAndSkipUnknownIds()
    {
        // Arrange
        var api = ApiWithChats();
        var client = api.CreateClient();
        var first = await client.SendMessage(Chat, "first");
        await client.SendMessage(Chat, "second");

        // Act
        await client.DeleteMessages(Chat, [first.Id, 999]);

        // Assert
        api.MessagesIn(Chat).Select(x => x["text"]!.GetValue<string>()).Should().Equal("second");
    }

    [Fact]
    public async Task DeleteMessages_WhenOneCannotBeDeleted_ShouldDeleteNothing()
    {
        // Arrange: in a group, the bot may delete only its own messages without admin rights.
        var api = new FakeBotApi();
        var client = api.CreateClient();
        var family = api.Group("Family");
        var own = await client.SendMessage(family, "Pick one");
        var annas = api.Receive(family, api.Person("Anna"), "hello")["message_id"]!.GetValue<int>();

        // Act
        var act = () => client.DeleteMessages(family, [own.Id, annas]);

        // Assert
        using (new AssertionScope())
        {
            (await act.Should().ThrowAsync<ApiRequestException>())
                .Which.Message.Should()
                .Be("Bad Request: message can't be deleted");
            api.MessagesIn(family).Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task DeleteMessages_WithAnEmptyList_ShouldSucceed()
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var body = new JsonObject { ["chat_id"] = Chat, ["message_ids"] = new JsonArray() };

        // Act
        var deleted = await client.SendRequest(new RawRequest<bool>("deleteMessages", body));

        // Assert
        deleted.Should().BeTrue();
    }

    [Theory]
    [InlineData("absent", "Bad Request: message identifiers are not specified")]
    [InlineData("101", "Bad Request: too many message identifiers specified")]
    [InlineData("0", "Bad Request: invalid message identifier specified")]
    [InlineData("-1", "Bad Request: invalid message identifier specified")]
    public async Task DeleteMessages_ShouldBeRefusedLikeTelegram(string ids, string description)
    {
        // Arrange
        var client = ApiWithChats().CreateClient();
        var body = new JsonObject { ["chat_id"] = Chat };
        body["message_ids"] = ids switch
        {
            "absent" => null,
            "101" => new JsonArray([.. Enumerable.Range(1, 101).Select(id => (JsonNode)id)]),
            _ => new JsonArray(int.Parse(ids)),
        };

        // Act
        var act = () => client.SendRequest(new RawRequest<bool>("deleteMessages", body));

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be(description);
    }
}
