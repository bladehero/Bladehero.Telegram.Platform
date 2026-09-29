using System.Diagnostics;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class LongPollingTests
{
    [Fact]
    public async Task Startup_WithoutABaseUrl_ShouldPollAndSetNoWebhook()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(BotMode.LongPolling);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            bot.Api.WebhookUrl.Should().BeNull();
            nick.LastMessage.Text.Should().Be("Reply: hello");
        }
    }

    [Fact]
    public async Task Startup_WithALeftoverWebhook_ShouldDeleteItAndPoll()
    {
        // Arrange
        var api = new FakeBotApi();
        await api.CreateClient().SetWebhook("https://old.example.com/telegram/updates");

        // Act
        await using var bot = await SandboxBot.StartAsync(BotMode.LongPolling, api);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            api.WebhookUrl.Should().BeNull();
            nick.LastMessage.Text.Should().Be("Reply: hello");
        }
    }

    [Fact]
    public async Task SendAsync_WhenTheAppsBackgroundServiceFails_ShouldFailFastWithTheCause()
    {
        // Arrange
        var importer = new FailingImporter();
        await using var bot = await SandboxBot.StartAsync(
            BotMode.LongPolling,
            configure: web => web.ConfigureTestServices(services => services.AddHostedService(_ => importer))
        );
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bot.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => stopping.TrySetResult());
        var cause = new InvalidOperationException("The import queue is gone");
        importer.Fail(cause);
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        // Act
        var failure = await Record.ExceptionAsync(() => bot.PrivateChat("Nick").SendsAsync("hello"));

        // Assert
        using (new AssertionScope())
        {
            failure.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("host stopped");
            failure?.InnerException.Should().BeSameAs(cause);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the update timeout is 30 s");
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WhenTheAppSetsAWebhook_ShouldSayToStartItWithTheWebhookHost()
    {
        // Arrange: with a base URL the app sets a webhook.
        await using var bot = await TelegramTestHost.ForLongPollingAsync<Program>(web =>
            SandboxBot.Configure(web, BotMode.Webhook)
        );
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.PrivateChat("Nick").SendsAsync("hello");

        // Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage(
                $"*The app set a webhook, {SandboxBot.BaseUrl}/telegram/updates, and does not poll; start it with "
                    + "ForWebhookAsync<Program>."
            );
    }

    // A background service of the app's that fails when told to.
    private sealed class FailingImporter : BackgroundService
    {
        private readonly TaskCompletionSource _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Fail(Exception cause) => _failure.SetException(cause);

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => _failure.Task.WaitAsync(stoppingToken);
    }
}
