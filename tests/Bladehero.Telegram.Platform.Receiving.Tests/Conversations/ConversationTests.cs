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

    [Fact]
    public void Key_ShouldBeTheUserInTheChatOfTheUpdate()
    {
        // Act
        var key = Bound(new InMemoryConversationStore()).Key;

        // Assert
        key.Should().Be(Sender);
    }

    [Fact]
    public async Task BindAsync_WithoutAConversation_ShouldThrow()
    {
        // Arrange
        var sut = Bound(new InMemoryConversationStore());

        // Act
        var act = () => sut.BindAsync(CancellationToken.None).AsTask();

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("There is no active conversation to bind buttons to; start one first.");
    }

    [Fact]
    public async Task BindAsync_WithoutAKey_ShouldThrow()
    {
        // Arrange: an IConversation of the app's own, which leaves Key to its default.
        var sut = new Mock<IConversation>();
        sut.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ConversationState?>(new ConversationState("order", "size")));

        // Act
        var act = () => sut.Object.BindAsync(CancellationToken.None).AsTask();

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("This update has no user in a chat to bind buttons to.");
    }

    [Fact]
    public async Task BindAsync_ShouldGiveTheConversationAnIdAndSaveItOnce()
    {
        // Arrange
        var store = new Mock<IConversationStore>();
        store
            .Setup(x => x.GetAsync(Sender, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationState("order", "size"));
        var sut = Bound(store.Object);

        // Act
        var binding = await sut.BindAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            binding.UserId.Should().Be(Sender.UserId);
            binding.ConversationId.Should().MatchRegex("^[a-z0-9]{8}$");
            store.Verify(
                x =>
                    x.SaveAsync(
                        Sender,
                        new ConversationState("order", "size") { Id = binding.ConversationId },
                        It.IsAny<CancellationToken>()
                    ),
                Times.Once
            );
        }
    }

    [Fact]
    public async Task BindAsync_Twice_ShouldGiveTheSameBinding()
    {
        // Arrange
        var store = new Mock<IConversationStore>();
        store
            .Setup(x => x.GetAsync(Sender, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationState("order", "size"));
        var sut = Bound(store.Object);

        // Act
        var first = await sut.BindAsync(CancellationToken.None);
        var second = await sut.BindAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            second.Should().Be(first);
            store.Verify(
                x => x.SaveAsync(Sender, It.IsAny<ConversationState>(), It.IsAny<CancellationToken>()),
                Times.Once
            );
        }
    }

    [Fact]
    public async Task MoveToAsync_ShouldKeepTheId()
    {
        // Arrange
        var store = new InMemoryConversationStore();
        await Bound(store).StartAsync("order", "size", new Order("large"), CancellationToken.None);
        var binding = await Bound(store).BindAsync(CancellationToken.None);

        // Act
        await Bound(store).MoveToAsync("name", new Order("small"), CancellationToken.None);

        // Assert
        (await store.GetAsync(Sender, CancellationToken.None))!
            .Id.Should()
            .Be(binding.ConversationId);
    }

    [Fact]
    public async Task StartAsync_ShouldStartANewRunWithoutAnId()
    {
        // Arrange
        var store = new InMemoryConversationStore();
        await Bound(store).StartAsync("order", "size", CancellationToken.None);
        await Bound(store).BindAsync(CancellationToken.None);

        // Act
        await Bound(store).StartAsync("order", "size", CancellationToken.None);

        // Assert
        (await store.GetAsync(Sender, CancellationToken.None))!
            .Id.Should()
            .BeNull();
    }

    private static Conversation Bound(IConversationStore store)
    {
        var conversation = new Conversation(store);
        conversation.Bind(FromSender);
        return conversation;
    }

    private sealed record Order(string Size, int Shots = 1);
}
