using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationKeyTests
{
    private static readonly Chat Chat = new() { Id = 1 };
    private static readonly User Sender = new() { Id = 7 };

    [Theory]
    [InlineData(UpdateType.Message)]
    [InlineData(UpdateType.EditedMessage)]
    [InlineData(UpdateType.CallbackQuery)]
    public void For_WhenTheUpdateHasAUserInAChat_ShouldKeyItByBoth(UpdateType type)
    {
        // Act
        var key = ConversationKey.For(UpdateOf(type));

        // Assert
        key.Should().Be(new ConversationKey(ChatId: 1, UserId: 7));
    }

    [Theory]
    [InlineData(UpdateType.ChannelPost)]
    [InlineData(UpdateType.InlineQuery)]
    public void For_WhenTheUpdateHasNoUserInAChat_ShouldReturnNull(UpdateType type)
    {
        // Act
        var key = ConversationKey.For(UpdateOf(type));

        // Assert
        key.Should().BeNull();
    }

    private static Update UpdateOf(UpdateType type) =>
        type switch
        {
            UpdateType.Message => new Update
            {
                Message = new Message { Chat = Chat, From = Sender },
            },
            UpdateType.EditedMessage => new Update
            {
                EditedMessage = new Message { Chat = Chat, From = Sender },
            },
            UpdateType.CallbackQuery => new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "query",
                    From = Sender,
                    Message = new Message { Chat = Chat },
                },
            },
            UpdateType.ChannelPost => new Update { ChannelPost = new Message { Chat = Chat } },
            UpdateType.InlineQuery => new Update
            {
                InlineQuery = new InlineQuery
                {
                    Id = "query",
                    From = Sender,
                    Query = "",
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
}
