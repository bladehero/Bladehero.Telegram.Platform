using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task GetMe_ShouldSayWhetherTopicsAreOn()
    {
        // Arrange
        var api = new FakeBotApi { TopicsEnabled = true };

        // Act
        var me = await api.CreateClient().GetMe();

        // Assert
        me.HasTopicsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task CreateForumTopic_WithTopicsOff_ShouldBeRefused()
    {
        // Arrange
        var api = ApiWithChats();

        // Act
        var create = () => api.CreateClient().CreateForumTopic(Chat, "Питание");

        // Assert
        (await create.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("the chat is not a forum");
    }

    [Fact]
    public async Task SendMessage_InATopic_ShouldComeBackWithItsThread()
    {
        // Arrange
        var api = ApiWithChats();
        api.TopicsEnabled = true;
        var client = api.CreateClient();
        var topic = await client.CreateForumTopic(Chat, "Питание");

        // Act
        var sent = await client.SendMessage(Chat, "В теме", messageThreadId: topic.MessageThreadId);

        // Assert
        using (new AssertionScope())
        {
            sent.MessageThreadId.Should().Be(topic.MessageThreadId);
            sent.IsTopicMessage.Should().BeTrue();
            api.TopicsIn(Chat).Should().ContainSingle().Which.Should().Be(topic.MessageThreadId);
        }
    }

    [Fact]
    public async Task SendMessage_ToATopicDeletedByHand_ShouldBeRefused()
    {
        // Arrange
        var api = ApiWithChats();
        api.TopicsEnabled = true;
        var client = api.CreateClient();
        var topic = await client.CreateForumTopic(Chat, "Питание");
        api.DeleteTopicByHand(Chat, topic.MessageThreadId);

        // Act
        var send = () => client.SendMessage(Chat, "Куда?", messageThreadId: topic.MessageThreadId);

        // Assert
        (await send.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Contain("message thread not found");
    }

    [Fact]
    public async Task DeleteForumTopic_ShouldCloseIt()
    {
        // Arrange
        var api = ApiWithChats();
        api.TopicsEnabled = true;
        var client = api.CreateClient();
        var topic = await client.CreateForumTopic(Chat, "Питание");

        // Act
        await client.DeleteForumTopic(Chat, topic.MessageThreadId);

        // Assert
        api.TopicsIn(Chat).Should().BeEmpty();
    }
}
