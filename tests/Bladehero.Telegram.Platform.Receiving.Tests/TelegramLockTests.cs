using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

public sealed class TelegramLockTests
{
    private const string Reentered =
        "This code already runs under the Telegram lock (update handling or another RunAsync), so RunAsync would "
        + "wait for itself. Do the work directly here. Work started for later must not inherit the update's context: "
        + "start it with ExecutionContext.SuppressFlow().";

    // Only bounds a failing test's wait; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_ShouldRunTheWorkInAScopeOfItsOwn_DisposedBeforeTheLockIsReleased()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        await using var callers = provider.CreateAsyncScope();
        var callersProbe = callers.ServiceProvider.GetRequiredService<ScopedProbe>();
        ScopedProbe? worksProbe = null;

        // Act
        await sut.RunAsync(
            (services, _) =>
            {
                worksProbe = services.GetRequiredService<ScopedProbe>();
                return Task.CompletedTask;
            }
        );

        // Assert
        using (new AssertionScope())
        {
            worksProbe.Should().NotBeNull().And.NotBeSameAs(callersProbe);
            worksProbe!.HeldWhenDisposed.Should().BeTrue();
            callersProbe.HeldWhenDisposed.Should().BeNull();
        }
    }

    [Fact]
    public async Task RunAsync_Twice_ShouldGiveEachItsOwnScope()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();

        // Act
        var first = await sut.RunAsync((services, _) => Task.FromResult(services.GetRequiredService<ScopedProbe>()));
        var second = await sut.RunAsync((services, _) => Task.FromResult(services.GetRequiredService<ScopedProbe>()));

        // Assert
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task RunAsync_ShouldReturnTheWorksResult()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();

        // Act
        var result = await sut.RunAsync((_, _) => Task.FromResult(42));

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_ShouldGiveTheWorkTheCallersToken()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        using var cancellation = new CancellationTokenSource();

        // Act
        var given = await sut.RunAsync((_, token) => Task.FromResult(token), cancellation.Token);

        // Assert
        given.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task RunAsync_WhenTheWorkThrows_ShouldRethrowItAndRelease()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var failure = new InvalidOperationException("The database is down");

        // Act
        var thrown = await Record.ExceptionAsync(() => sut.RunAsync((_, _) => Task.FromException(failure)));
        var next = await sut.RunAsync((_, _) => Task.FromResult("ran")).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeSameAs(failure);
            next.Should().Be("ran");
            sut.IsHeld.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RunAsync_WhileOtherWorkHoldsIt_ShouldStartOnlyOnceThatWorkEnds()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var events = new ConcurrentQueue<string>();
        var release = Gate();
        var first = sut.RunAsync(
            async (_, _) =>
            {
                events.Enqueue("first starts");
                await release.Task;
                events.Enqueue("first ends");
            }
        );

        // Act
        var second = sut.RunAsync(
            (_, _) =>
            {
                events.Enqueue("second starts");
                return Task.CompletedTask;
            }
        );
        var secondWaited = sut.WaitingCount == 1 && !second.IsCompleted;
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            secondWaited.Should().BeTrue();
            events.Should().Equal("first starts", "first ends", "second starts");
        }
    }

    [Fact]
    public async Task RunAsync_ManyAtOnce_ShouldNeverRunTwoAtATime()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var inside = 0;
        var overlapped = false;
        var ran = 0;
        Func<IServiceProvider, CancellationToken, Task> work = async (_, _) =>
        {
            if (Interlocked.Increment(ref inside) > 1)
            {
                Volatile.Write(ref overlapped, true);
            }

            await Task.Yield();
            Interlocked.Increment(ref ran);
            Interlocked.Decrement(ref inside);
        };

        // Act
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => sut.RunAsync(work)))).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            overlapped.Should().BeFalse();
            ran.Should().Be(50);
        }
    }

    [Fact]
    public async Task RunAsync_WhileHeld_ShouldServeTheWaitersInArrivalOrder()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var order = new ConcurrentQueue<string>();
        var hold = await sut.HoldAsync(CancellationToken.None);
        var waiting = new[] { "first", "second", "third" }
            .Select(name =>
                sut.RunAsync(
                    (_, _) =>
                    {
                        order.Enqueue(name);
                        return Task.CompletedTask;
                    }
                )
            )
            .ToList();
        var queued = sut.WaitingCount;

        // Act
        await hold.DisposeAsync();
        await Task.WhenAll(waiting).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            queued.Should().Be(3);
            order.Should().Equal("first", "second", "third");
        }
    }

    [Fact]
    public async Task RunAsync_FromItsOwnWork_ShouldThrowRatherThanWaitForItself()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();

        // Act
        var act = () =>
            sut.RunAsync((_, token) => sut.RunAsync((_, _) => Task.CompletedTask, token)).WaitAsync(Patience);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(Reentered);
        sut.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_FromATaskTheHolderStartedWithoutAwaiting_BeforeItReleases_ShouldThrow()
    {
        // Arrange: the holder waits only until the task has called RunAsync, so waiting would have been safe; the
        // documented false alarm.
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var called = Gate();
        Task? early = null;

        // Act
        await sut.RunAsync(
                async (_, _) =>
                {
                    _ = Task.Run(() =>
                    {
                        early = sut.RunAsync((_, _) => Task.CompletedTask);
                        called.SetResult();
                    });
                    await called.Task;
                }
            )
            .WaitAsync(Patience);

        // Assert
        var act = () => early!;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(Reentered);
    }

    [Fact]
    public async Task RunAsync_FromATaskTheHolderStartedUnderSuppressFlow_ShouldWaitForTheHolderAndRun()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var called = Gate();
        var ran = false;
        var waitedWhileHeld = false;
        Task? later = null;

        // Act
        await sut.RunAsync(
                async (_, _) =>
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _ = Task.Run(() =>
                        {
                            later = sut.RunAsync(
                                (_, _) =>
                                {
                                    ran = true;
                                    return Task.CompletedTask;
                                }
                            );
                            called.SetResult();
                        });
                    }

                    await called.Task;
                    waitedWhileHeld = sut.WaitingCount == 1 && !ran;
                }
            )
            .WaitAsync(Patience);
        await later!.WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            waitedWhileHeld.Should().BeTrue();
            ran.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RunAsync_FromATaskTheHolderStartedWithoutAwaiting_AfterItReleased_ShouldRun()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var holderReturned = Gate();
        var ran = false;
        Task? later = null;
        await sut.RunAsync(
            (_, _) =>
            {
                later = Task.Run(async () =>
                {
                    await holderReturned.Task;
                    await sut.RunAsync(
                        (_, _) =>
                        {
                            ran = true;
                            return Task.CompletedTask;
                        }
                    );
                });
                return Task.CompletedTask;
            }
        );

        // Act
        holderReturned.SetResult();
        await later!.WaitAsync(Patience);

        // Assert
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTheCallerCancelsTheWait_ShouldThrowWithoutRunningTheWork()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        await using var hold = await sut.HoldAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var ran = false;
        var waiting = sut.RunAsync(
            (_, _) =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            cancellation.Token
        );

        // Act
        await cancellation.CancelAsync();
        var act = () => waiting.WaitAsync(Patience);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        using (new AssertionScope())
        {
            ran.Should().BeFalse();
            sut.WaitingCount.Should().Be(0);
            sut.IsHeld.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RunAsync_WhenTheHostBeginsToStopDuringTheWait_ShouldThrowWithoutRunningTheWork()
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        await using var provider = Provider(stopping.Token);
        var sut = provider.GetRequiredService<TelegramLock>();
        await using var hold = await sut.HoldAsync(CancellationToken.None);
        var ran = false;
        var waiting = sut.RunAsync(
            (_, _) =>
            {
                ran = true;
                return Task.CompletedTask;
            }
        );

        // Act
        await stopping.CancelAsync();
        var act = () => waiting.WaitAsync(Patience);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        using (new AssertionScope())
        {
            ran.Should().BeFalse();
            sut.WaitingCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task RunAsync_OnceTheHostBeganToStop_ShouldNotStartTheWork()
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        await using var provider = Provider(stopping.Token);
        var sut = provider.GetRequiredService<TelegramLock>();
        await stopping.CancelAsync();
        var ran = false;

        // Act
        var act = () =>
            sut.RunAsync(
                (_, _) =>
                {
                    ran = true;
                    return Task.CompletedTask;
                }
            );

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        using (new AssertionScope())
        {
            ran.Should().BeFalse();
            sut.IsHeld.Should().BeFalse();
        }
    }

    [Fact]
    public async Task IsHeldAndWaitingCount_ShouldFollowTheHolderAndTheWaiters()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var before = (sut.IsHeld, sut.WaitingCount);
        var release = Gate();
        var holder = sut.RunAsync((_, _) => release.Task);
        var whileHeld = (sut.IsHeld, sut.WaitingCount);
        var waiter = sut.RunAsync((_, _) => Task.CompletedTask);
        var withAWaiter = (sut.IsHeld, sut.WaitingCount);

        // Act
        release.SetResult();
        await Task.WhenAll(holder, waiter).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            before.Should().Be((false, 0));
            whileHeld.Should().Be((true, 0));
            withAWaiter.Should().Be((true, 1));
            (sut.IsHeld, sut.WaitingCount).Should().Be((false, 0));
        }
    }

    [Fact]
    public async Task Release_ToAWaiter_ShouldCountItAsHoldingAtOnce()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var hold = await sut.HoldAsync(CancellationToken.None);
        var release = Gate();
        var first = sut.RunAsync((_, _) => release.Task);
        var second = sut.RunAsync((_, _) => Task.CompletedTask);

        // Act
        await hold.DisposeAsync();
        var rightAfter = (sut.IsHeld, sut.WaitingCount);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Patience);

        // Assert
        rightAfter.Should().Be((true, 1));
    }

    [Fact]
    public async Task RunAsync_CancelledAsTheLockIsHandedToIt_ShouldEitherRunOrGiveUp_NeverLeakTheLock()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var outcomes = new List<string>();

        // Act
        for (var round = 0; round < 200; round++)
        {
            var hold = await sut.HoldAsync(CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var ran = false;
            var waiting = sut.RunAsync(
                (_, _) =>
                {
                    ran = true;
                    return Task.CompletedTask;
                },
                cancellation.Token
            );
            await Task.WhenAll(Task.Run(cancellation.Cancel), Task.Run(() => hold.DisposeAsync().AsTask()));
            var thrown = await Record.ExceptionAsync(() => waiting.WaitAsync(Patience));
            outcomes.Add(
                thrown is OperationCanceledException && !ran ? "gave up"
                : thrown is null && ran ? "ran"
                : "?"
            );
        }

        // Assert
        using (new AssertionScope())
        {
            outcomes.Should().NotContain("?");
            (sut.IsHeld, sut.WaitingCount).Should().Be((false, 0));
        }
    }

    [Fact]
    public async Task Changed_ShouldCompleteAtTheNextChangeOnly()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var changed = sut.Changed;
        var unchangedAtFirst = !changed.IsCompleted;

        // Act
        await using var hold = await sut.HoldAsync(CancellationToken.None);
        await changed.WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            unchangedAtFirst.Should().BeTrue();
            sut.Changed.IsCompleted.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RunAsync_StartedWhileAnUpdateIsTheHistorysCause_ShouldLinkTheWorksCallsToNoUpdate()
    {
        // Arrange
        await using var provider = Provider();
        var sut = provider.GetRequiredService<TelegramLock>();
        var update = new Update { Id = 7 };
        Update? causeInTheWork = update;
        Update? causeAfterwards;

        // Act
        using (TelegramHistoryCause.Begin(update))
        {
            await sut.RunAsync(
                (_, _) =>
                {
                    causeInTheWork = TelegramHistoryCause.Current;
                    return Task.CompletedTask;
                }
            );
            causeAfterwards = TelegramHistoryCause.Current;
        }

        // Assert
        using (new AssertionScope())
        {
            causeInTheWork.Should().BeNull();
            causeAfterwards.Should().BeSameAs(update);
        }
    }

    // A lock with a scoped probe; with `stopping`, a host whose stop begins when it's cancelled.
    private static ServiceProvider Provider(CancellationToken? stopping = null)
    {
        var services = new ServiceCollection().AddTelegramLock();
        services.AddScoped<ScopedProbe>();
        if (stopping is { } token)
        {
            services.AddSingleton(Mock.Of<IHostApplicationLifetime>(x => x.ApplicationStopping == token));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Records whether the lock was held when its scope disposed it; null until then.
    private sealed class ScopedProbe(TelegramLock telegramLock) : IDisposable
    {
        public bool? HeldWhenDisposed { get; private set; }

        public void Dispose() => HeldWhenDisposed = telegramLock.IsHeld;
    }
}
