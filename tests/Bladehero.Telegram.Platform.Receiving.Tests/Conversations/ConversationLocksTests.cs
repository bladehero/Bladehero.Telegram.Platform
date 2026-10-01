using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationLocksTests
{
    private static readonly ConversationKey Nick = new(ChatId: 1, UserId: 7);
    private static readonly ConversationKey Anna = new(ChatId: 1, UserId: 8);

    [Fact]
    public async Task EnterAsync_ForOneKey_ShouldLetOneInAtATime()
    {
        // Arrange
        var sut = new ConversationLocks();
        var first = await sut.EnterAsync(Nick, CancellationToken.None);

        // Act
        var second = sut.EnterAsync(Nick, CancellationToken.None).AsTask();
        var waitedWhileHeld = !second.IsCompleted;
        await first.DisposeAsync();
        var entered = await second.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        using (new AssertionScope())
        {
            waitedWhileHeld.Should().BeTrue();
            sut.UsersOf(Nick).Should().Be(1);
        }

        await entered.DisposeAsync();
    }

    [Fact]
    public async Task EnterAsync_ForTwoKeys_ShouldNotWaitForEachOther()
    {
        // Arrange
        var sut = new ConversationLocks();
        await using var nick = await sut.EnterAsync(Nick, CancellationToken.None);

        // Act
        var anna = sut.EnterAsync(Anna, CancellationToken.None);

        // Assert
        anna.IsCompletedSuccessfully.Should().BeTrue();
        await (await anna).DisposeAsync();
    }

    [Fact]
    public async Task EnterAsync_WhenCancelled_ShouldStopWaiting()
    {
        // Arrange
        var sut = new ConversationLocks();
        await using var held = await sut.EnterAsync(Nick, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = sut.EnterAsync(Nick, cancellation.Token).AsTask();

        // Act
        await cancellation.CancelAsync();
        var act = () => waiting;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.UsersOf(Nick).Should().Be(1);
    }

    [Fact]
    public async Task EnterAsync_AfterEveryoneLeft_ShouldForgetTheKey()
    {
        // Arrange
        var sut = new ConversationLocks();
        var first = await sut.EnterAsync(Nick, CancellationToken.None);
        var second = sut.EnterAsync(Nick, CancellationToken.None).AsTask();

        // Act
        await first.DisposeAsync();
        await (await second).DisposeAsync();

        // Assert
        sut.Count.Should().Be(0);
    }
}
