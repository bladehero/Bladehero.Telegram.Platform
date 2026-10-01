using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

public sealed class TelegramBotIdentityTests
{
    private static readonly User Bot = new()
    {
        Id = 1234567,
        IsBot = true,
        FirstName = "Test Bot",
        Username = "test_bot",
    };

    [Fact]
    public async Task GetAsync_ShouldCallGetMeOnceForConcurrentCallers()
    {
        // Arrange
        var answer = new TaskCompletionSource<User>();
        var client = ClientAnswering(answer.Task);
        var sut = new TelegramBotIdentity(client.Object);
        var first = sut.GetAsync(CancellationToken.None).AsTask();
        var second = sut.GetAsync(CancellationToken.None).AsTask();

        // Act
        answer.SetResult(Bot);
        var users = await Task.WhenAll(first, second);

        // Assert
        using (new AssertionScope())
        {
            users.Should().AllSatisfy(x => x.Should().BeSameAs(Bot));
            client.Verify(GetMe(), Times.Once);
        }
    }

    [Fact]
    public async Task GetAsync_AfterAFailure_ShouldTryAgain()
    {
        // Arrange
        var client = ClientAnswering(Task.FromException<User>(new HttpRequestException("down")), Task.FromResult(Bot));
        var sut = new TelegramBotIdentity(client.Object);
        await Record.ExceptionAsync(() => sut.GetAsync(CancellationToken.None).AsTask());

        // Act
        var user = await sut.GetAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            user.Should().BeSameAs(Bot);
            client.Verify(GetMe(), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task TryGetAsync_AfterAFailure_ShouldWaitAMinuteBeforeTryingAgain()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var client = ClientAnswering(Task.FromException<User>(new HttpRequestException("down")), Task.FromResult(Bot));
        var sut = new TelegramBotIdentity(client.Object, time);
        await sut.TryGetAsync(CancellationToken.None);

        // Act
        var tooSoon = await sut.TryGetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        var aMinuteOn = await sut.TryGetAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            tooSoon.Should().BeNull();
            aMinuteOn.Should().BeSameAs(Bot);
            client.Verify(GetMe(), Times.Exactly(2));
        }
    }

    [Fact]
    public async Task Current_ShouldBeNullUntilGetMeSucceeds()
    {
        // Arrange
        var client = ClientAnswering(Task.FromException<User>(new HttpRequestException("down")), Task.FromResult(Bot));
        var sut = new TelegramBotIdentity(client.Object);
        await Record.ExceptionAsync(() => sut.GetAsync(CancellationToken.None).AsTask());
        var afterAFailure = sut.Current;

        // Act
        await sut.GetAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            afterAFailure.Should().BeNull();
            sut.Current.Should().BeSameAs(Bot);
        }
    }

    private static Mock<ITelegramBotClient> ClientAnswering(params Task<User>[] answers)
    {
        var client = new Mock<ITelegramBotClient>();
        var sequence = client.SetupSequence(GetMe());
        foreach (var answer in answers)
        {
            sequence = sequence.Returns(answer);
        }

        return client;
    }

    private static System.Linq.Expressions.Expression<Func<ITelegramBotClient, Task<User>>> GetMe() =>
        x => x.SendRequest(It.IsAny<IRequest<User>>(), It.IsAny<CancellationToken>());
}
