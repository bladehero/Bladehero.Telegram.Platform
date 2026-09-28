using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationTests
{
    private static readonly ConversationKey Sender = new(ChatId: 1, UserId: 7);

    private static readonly Update FromSender = new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = 1 },
            From = new User { Id = 7 },
        },
    };

    [Fact]
    public async Task GetAsync_WhenCalledRepeatedly_ShouldReadTheStoreOnce()
    {
        // Arrange
        var store = new Mock<IConversationStore>();
        var sut = Bound(store.Object);

        // Act
        await sut.GetAsync(CancellationToken.None);
        await sut.GetAsync(CancellationToken.None);

        // Assert
        store.Verify(x => x.GetAsync(Sender, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldSaveUnderTheSendersChatAndUser()
    {
        // Arrange
        var store = new InMemoryConversationStore();
        var sut = Bound(store);

        // Act
        await sut.StartAsync("order", "size", CancellationToken.None);

        // Assert
        (await store.GetAsync(Sender, CancellationToken.None))
            .Should()
            .Be(new ConversationState("order", "size"));
    }

    [Fact]
    public async Task MoveToAsync_WithoutData_ShouldKeepTheData()
    {
        // Arrange
        var sut = Bound(new InMemoryConversationStore());
        await sut.StartAsync("order", "size", new Order("large"), CancellationToken.None);

        // Act
        await sut.MoveToAsync("name", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            (await sut.GetAsync(CancellationToken.None))!.Step.Should().Be("name");
            (await sut.GetDataAsync<Order>(CancellationToken.None)).Should().Be(new Order("large"));
        }
    }

    [Fact]
    public async Task MoveToAsync_WithData_ShouldReplaceTheData()
    {
        // Arrange
        var sut = Bound(new InMemoryConversationStore());
        await sut.StartAsync("order", "size", new Order("large"), CancellationToken.None);

        // Act
        await sut.MoveToAsync("name", new Order("small", Shots: 2), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            (await sut.GetAsync(CancellationToken.None))!.Step.Should().Be("name");
            (await sut.GetDataAsync<Order>(CancellationToken.None)).Should().Be(new Order("small", Shots: 2));
        }
    }

    [Fact]
    public async Task MoveToAsync_WhenNoConversationIsActive_ShouldThrow()
    {
        // Arrange
        var sut = Bound(new InMemoryConversationStore());

        // Act
        var act = () => sut.MoveToAsync("name", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetDataAsync_OnTheNextUpdate_ShouldReadBackTheTypedData()
    {
        // Arrange
        var store = new InMemoryConversationStore();
        await Bound(store).StartAsync("order", "size", new Order("large", Shots: 2), CancellationToken.None);

        // Act
        var data = await Bound(store).GetDataAsync<Order>(CancellationToken.None);

        // Assert
        data.Should().Be(new Order("large", Shots: 2));
    }

    [Fact]
    public async Task EndAsync_ShouldRemoveTheConversation()
    {
        // Arrange
        var store = new InMemoryConversationStore();
        await Bound(store).StartAsync("order", "size", CancellationToken.None);

        // Act
        await Bound(store).EndAsync(CancellationToken.None);

        // Assert
        (await store.GetAsync(Sender, CancellationToken.None))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenTheUpdateHasNoUserInAChat_ShouldThrow()
    {
        // Arrange
        var sut = new Conversation(new InMemoryConversationStore());
        sut.Bind(
            new Update
            {
                InlineQuery = new InlineQuery
                {
                    Id = "query",
                    From = new User { Id = 7 },
                    Query = "",
                },
            }
        );

        // Act
        var act = () => sut.StartAsync("order", "size", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static Conversation Bound(IConversationStore store)
    {
        var conversation = new Conversation(store);
        conversation.Bind(FromSender);
        return conversation;
    }

    private sealed record Order(string Size, int Shots = 1);
}
