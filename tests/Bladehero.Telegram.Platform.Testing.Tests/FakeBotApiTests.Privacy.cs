using FluentAssertions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task DeleteMessage_OfAUsersMessageInAGroupWithoutAdminRights_ShouldBeRefused(
        bool admin,
        bool canDeleteMessages
    )
    {
        // Arrange
        var api = new FakeBotApi();
        var family = api.Group("Family");
        var hello = api.Receive(family, api.Person("Anna"), "hello");
        if (admin)
        {
            api.SetAdmin(family, admin: true, canDeleteMessages);
        }

        // Act
        var act = () => api.CreateClient().DeleteMessage(family, hello["message_id"]!.GetValue<int>());

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.Message.Should()
            .Be("Bad Request: message can't be deleted");
    }

    [Fact]
    public async Task DeleteMessage_OfAUsersMessageInAGroupAsAnAdminThatMayDelete_ShouldSucceed()
    {
        // Arrange
        var api = new FakeBotApi();
        var family = api.Group("Family");
        var hello = api.Receive(family, api.Person("Anna"), "hello");
        api.SetAdmin(family, admin: true, canDeleteMessages: true);

        // Act
        await api.CreateClient().DeleteMessage(family, hello["message_id"]!.GetValue<int>());

        // Assert
        api.MessagesIn(family).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMessage_OfAUsersMessageInAPrivateChat_ShouldSucceed()
    {
        // Arrange
        var api = ApiWithChats();
        var hello = api.Receive(Chat, Nick(), "hello");

        // Act
        await api.CreateClient().DeleteMessage(Chat, hello["message_id"]!.GetValue<int>());

        // Assert
        api.MessagesIn(Chat).Should().BeEmpty();
    }
}
