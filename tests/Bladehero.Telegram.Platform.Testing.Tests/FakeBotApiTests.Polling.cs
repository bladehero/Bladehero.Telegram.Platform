using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    [Fact]
    public async Task GetUpdates_WhenASecondPollWaits_ShouldEndTheFirstWithAConflict()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();
        using var stop = new CancellationTokenSource();
        var first = client.GetUpdates(timeout: 30);
        await WaitingPollAsync(api);

        // Act
        var second = client.GetUpdates(timeout: 30, cancellationToken: stop.Token);
        var conflict = await Record.ExceptionAsync(() => first);

        // Assert
        using (new AssertionScope())
        {
            conflict.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(409);
            conflict!
                .Message.Should()
                .Be(
                    "Conflict: terminated by other getUpdates request; make sure that only one bot instance is running"
                );
            second.IsCompleted.Should().BeFalse();
        }

        await stop.CancelAsync();
        await Record.ExceptionAsync(() => second);
    }

    [Fact]
    public async Task GetUpdates_TheDropPendingPoll_ShouldNotEndAWaitingPoll()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();
        using var stop = new CancellationTokenSource();
        var waiting = client.GetUpdates(timeout: 30, cancellationToken: stop.Token);
        await WaitingPollAsync(api);

        // Act
        await client.GetUpdates(offset: -1, timeout: 0);

        // Assert
        using (new AssertionScope())
        {
            waiting.IsCompleted.Should().BeFalse();
            api.ConflictedPolling.Should().BeFalse();
        }

        await stop.CancelAsync();
        await Record.ExceptionAsync(() => waiting);
    }

    [Fact]
    public async Task GetUpdates_AfterTheWaitingPollIsCancelled_ShouldNotConflict()
    {
        // Arrange
        var api = new FakeBotApi();
        var client = api.CreateClient();
        using (var stop = new CancellationTokenSource())
        {
            var cancelled = client.GetUpdates(timeout: 30, cancellationToken: stop.Token);
            await WaitingPollAsync(api);
            await stop.CancelAsync();
            await Record.ExceptionAsync(() => cancelled);
        }

        var next = client.GetUpdates(timeout: 30);
        await WaitingPollAsync(api);

        // Act
        api.Enqueue(new JsonObject { ["message"] = new JsonObject { ["text"] = "hello" } });
        var updates = await next;

        // Assert
        using (new AssertionScope())
        {
            updates.Should().ContainSingle();
            api.ConflictedPolling.Should().BeFalse();
        }
    }

    // No timing: yields until a poll waits.
    private static async Task WaitingPollAsync(FakeBotApi api)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!api.PollWaiting)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
