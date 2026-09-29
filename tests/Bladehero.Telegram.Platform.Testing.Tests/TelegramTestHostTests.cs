using System.Diagnostics;
using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TelegramTestHostTests
{
    [Fact]
    public async Task SendAsync_ShouldRunTheUpdateThroughTheRealPollingLoop()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        await bot.SendAsync(Text("hello"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage").Which.Parameters["text"]!
            .GetValue<string>()
            .Should()
            .Be("hello");
    }

    [Fact]
    public async Task SendAsync_ShouldReturnOnlyOnceTheBotHasFinishedTheUpdate()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        await bot.SendAsync(Text("/slow"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
    }

    [Fact]
    public async Task SendAsync_WhenACommandThrows_ShouldRethrowItsException()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.SendAsync(Text("/boom"));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task SendAsync_AfterACommandThrew_ShouldKeepHandlingUpdates()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        await ((Func<Task>)(() => bot.SendAsync(Text("/boom")))).Should().ThrowAsync<InvalidOperationException>();

        // Act
        await bot.SendAsync(Text("still here"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
    }

    [Fact]
    public async Task SendAsync_WhenACommandThrowsACancellation_ShouldRethrowItAndKeepPolling()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var failure = await Record.ExceptionAsync(() => bot.SendAsync(Text("/timeout")));
        await bot.SendAsync(Text("still here"));

        // Assert
        using (new AssertionScope())
        {
            failure.Should().BeOfType<TaskCanceledException>().Which.Message.Should().Be("Claude timed out");
            bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
        }
    }

    [Fact]
    public async Task SendAsync_WhenTheCommandGraphCannotBeBuilt_ShouldRethrowTheCauseAndKeepPolling()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddScoped<ITelegramCommandExecutor>(_ => throw new InvalidOperationException("db down"))
        );
        var stopwatch = Stopwatch.StartNew();

        // Act
        var first = await Record.ExceptionAsync(() => bot.SendAsync(Text("hello")));
        var second = await Record.ExceptionAsync(() => bot.SendAsync(Text("hello again")));

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("db down");
            second.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("db down");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "neither waits out the 30 s update timeout");
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_ShouldRunTheHostsStartupAgainstTheFake()
    {
        // Act
        await using var bot = await TestBot.StartAsync();

        // Assert
        bot.Api.Calls.Select(x => x.Method).Should().Contain("getWebhookInfo");
    }

    [Fact]
    public async Task ForLongPollingAsync_ShouldReturnOnceTheCommandMenuIsPublished()
    {
        // Act
        await using var bot = await TestBot.StartAsync();

        // Assert
        bot.Api.CommandMenu()
            .Select(x => $"/{x.Command} {x.Description}")
            .Should()
            .Equal("/menu Show the menu", "/whoami Say who you are");
    }

    [Fact]
    public async Task ForLongPollingAsync_WithAnArrangedFake_ShouldStartAgainstIt()
    {
        // Arrange
        var api = new FakeBotApi();
        api.Fail("setMyCommands", new BotApiError(500, "Internal Server Error"));

        // Act
        await using var bot = await TestBot.StartAsync(api);

        // Assert
        using (new AssertionScope())
        {
            bot.Api.Should().BeSameAs(api);
            api.CommandMenu().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WithALeftoverWebhook_ShouldDeleteItAndPoll()
    {
        // Arrange
        var api = new FakeBotApi();
        await api.CreateClient().SetWebhook("https://bot.example.com/updates");

        // Act
        await using var bot = await TestBot.StartAsync(api);
        await bot.SendAsync(Text("hello"));

        // Assert
        using (new AssertionScope())
        {
            api.WebhookUrl.Should().BeNull();
            api.Calls.Should().Contain(x => x.Method == "sendMessage");
        }
    }

    [Fact]
    public async Task SendAsync_WhenTheLeftoverWebhookCannotBeDeleted_ShouldSayItBlocksPolling()
    {
        // Arrange
        var api = new FakeBotApi();
        await api.CreateClient().SetWebhook("https://bot.example.com/updates");
        api.Fail("deleteWebhook", new BotApiError(500, "Internal Server Error"));
        await using var bot = await TestBot.StartAsync(api);
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.SendAsync(Text("hello"));

        // Assert
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*still has a webhook*409*");
    }

    [Fact]
    public async Task SendAsync_WhenWebhookReceivingWasRegisteredInstead_ShouldSayLongPollingIsMissing()
    {
        // Arrange: a webhook is set, but nothing polls.
        await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramWebhookReceiving(
                webhook =>
                {
                    webhook.Token = "unused";
                    webhook.BaseUrl = "https://bot.example.com";
                    webhook.UpdateEndpoint = "updates";
                },
                typeof(TestBot).Assembly
            )
        );
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.SendAsync(Text("hello"));

        // Assert
        using (new AssertionScope())
        {
            bot.Api.WebhookUrl.Should().NotBeNull();
            (await act.Should().ThrowAsync<TimeoutException>())
                .Which.Message.Should()
                .Contain("AddTelegramLongPollingReceiving")
                .And.NotContain("409");
        }
    }

    [Fact]
    public async Task SendAsync_WhenLongPollingIsNotRegistered_ShouldSayWhatIsMissing()
    {
        // Arrange
        await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramReceiving(typeof(TestBot).Assembly)
        );
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.SendAsync(Text("hello"));

        // Assert
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*AddTelegramLongPollingReceiving*");
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopThePollingLoopWithoutWaitingOutItsLongPoll()
    {
        // Arrange
        var bot = await TestBot.StartAsync();
        var stopwatch = Stopwatch.StartNew();

        // Act
        await bot.DisposeAsync();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private static Update Text(string text) =>
        new()
        {
            Message = new Message
            {
                Id = 1,
                Date = DateTime.UtcNow,
                Text = text,
                Chat = new Chat { Id = 42, Type = ChatType.Private },
                From = new User { Id = 42, FirstName = "Nick" },
            },
        };
}
