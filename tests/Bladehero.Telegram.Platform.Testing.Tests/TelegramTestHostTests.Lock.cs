using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TelegramTestHostTests
{
    [Fact]
    public async Task HoldLockAsync_ShouldKeepAnUpdateWaitingUntilReleased()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithLock);
        var nick = bot.PrivateChat("Nick");
        var hold = await bot.HoldLockAsync();
        var sending = nick.SendsAsync("hi");
        await bot.WaitForLockWaitersAsync(1);
        var whileHeld = nick.Messages.Select(x => x.ToString()).ToList();

        // Act
        await hold.DisposeAsync();
        await sending;

        // Assert
        using (new AssertionScope())
        {
            whileHeld.Should().Equal("Nick: hi");
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hi", "Bot: hi");
        }
    }

    [Fact]
    public async Task WaitForLockWaitersAsync_ShouldCountUpdatesAndWorkAlike_ServedInArrivalOrder()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithLock);
        var nick = bot.PrivateChat("Nick");
        var telegramLock = bot.Services.GetRequiredService<ITelegramLock>();
        var hold = await bot.HoldLockAsync();
        var sending = nick.SendsAsync("hi");
        await bot.WaitForLockWaitersAsync(1);
        var repliesBeforeTheWork = -1;
        var work = telegramLock.RunAsync(
            (_, _) =>
            {
                repliesBeforeTheWork = RepliesIn(bot.Api).Count();
                return Task.CompletedTask;
            }
        );

        // Act
        await bot.WaitForLockWaitersAsync(2);
        await hold.DisposeAsync();
        await Task.WhenAll(sending, work);

        // Assert
        repliesBeforeTheWork.Should().Be(1, "the update waited first, so it went first");
    }

    [Fact]
    public async Task SendsAsync_WhileRunAsyncWorkHoldsTheLock_ShouldBeHandledOnceTheWorkEnds()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithLock);
        var nick = bot.PrivateChat("Nick");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = bot.Services.GetRequiredService<ITelegramLock>().RunAsync((_, _) => release.Task);
        var sending = nick.SendsAsync("hi");
        await bot.WaitForLockWaitersAsync(1);
        var whileTheWorkRan = nick.Messages.Select(x => x.ToString()).ToList();

        // Act
        release.SetResult();
        await Task.WhenAll(work, sending);

        // Assert
        using (new AssertionScope())
        {
            whileTheWorkRan.Should().Equal("Nick: hi");
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hi", "Bot: hi");
        }
    }

    [Fact]
    public async Task WaitForLockWaitersAsync_WhenTooFewWait_ShouldTimeOutNamingWaitingCountAndIsHeld()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithLock);
        await using var hold = await bot.HoldLockAsync();

        // Act
        var act = () => bot.WaitForLockWaitersAsync(1, timeout: TimeSpan.FromMilliseconds(50));

        // Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage(
                "Fewer than 1 waited for the Telegram lock within 50 ms: WaitingCount is 0 and IsHeld is true."
            );
    }

    [Fact]
    public async Task HoldLockAsync_WhenTheLockIsNotFreed_ShouldTimeOutAndNotTakeItLater()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithLock);
        var telegramLock = bot.Services.GetRequiredService<ITelegramLock>();
        var first = await bot.HoldLockAsync();
        bot.UpdateTimeout = TimeSpan.FromMilliseconds(50);

        // Act
        var act = () => bot.HoldLockAsync();

        // Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage("The Telegram lock wasn't free within 50 ms: WaitingCount is 0 and IsHeld is true.*");
        await first.DisposeAsync();
        telegramLock.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task HoldLockAsync_WithoutAddTelegramLock_ShouldThrowNamingIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.HoldLockAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddTelegramLock()*");
    }

    [Fact]
    public async Task WaitForLockWaitersAsync_WithoutAddTelegramLock_ShouldThrowNamingIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.WaitForLockWaitersAsync(1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddTelegramLock()*");
    }

    [Fact]
    public async Task SendsAsync_WhenACommandCallsRunAsync_ShouldFailAtOnceWithTheReentryError()
    {
        // Arrange
        var seen = new SeenErrors();
        await using var bot = await TestBot.StartAsync(services: services =>
        {
            services.AddTelegramLock();
            services.AddSingleton(seen);
            services.AddScoped<ITelegramErrorHandler, SeenErrorsHandler>();
        });
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/relock"));
        await nick.SendsAsync("still here");

        // Assert
        using (new AssertionScope())
        {
            thrown
                .Should()
                .BeOfType<InvalidOperationException>()
                .Which.Message.Should()
                .StartWith("This code already runs under the Telegram lock")
                .And.EndWith("start it with ExecutionContext.SuppressFlow().");
            seen.All.Should().ContainSingle().Which.Exception.Should().BeSameAs(thrown);
            nick.LastMessage.Text.Should().Be("still here");
        }
    }

    [Fact]
    public async Task History_OfWorkACommandStartedThroughRunAsync_ShouldLinkItsCallsToNoUpdate()
    {
        // Arrange
        var notice = new TestBot.NoticeWork();
        await using var bot = await TestBot.StartAsync(services: services =>
        {
            services.AddTelegramLock();
            services.AddSingleton(notice);
            WithHistory(services);
        });
        var nick = bot.PrivateChat("Nick");
        var trigger = await nick.SendsAsync("/notice");

        // Act
        notice.Go.SetResult();
        await nick.WaitForMessageAsync(x => x.Text == "notice", after: trigger);

        // Assert
        var entries = await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id });
        entries.Should().ContainSingle(x => x.Kind == "sendMessage").Which.UpdateId.Should().BeNull();
    }

    private static void WithLock(IServiceCollection services) => services.AddTelegramLock();
}
