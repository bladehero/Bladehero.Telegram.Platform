using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class WebhookTests
{
    [Fact]
    public async Task Startup_ShouldSetTheWebhookToTheConfiguredAddress()
    {
        // Act
        await using var bot = await SandboxBot.StartAsync(BotMode.Webhook);

        // Assert
        bot.Api.WebhookUrl.Should().Be($"{SandboxBot.BaseUrl}/telegram/updates");
    }

    [Fact]
    public async Task Startup_ShouldCheckTheWebhookBeforeSettingIt()
    {
        // Act
        await using var bot = await SandboxBot.StartAsync(BotMode.Webhook);

        // Assert
        bot.Api.Calls.Select(x => x.Method).Should().ContainInOrder("getMe", "getWebhookInfo", "setWebhook");
    }

    [Fact]
    public async Task Update_ShouldArriveAsTelegramSendsIt()
    {
        // Arrange
        var requests = new WebhookRequests();
        await using var bot = await StartWatchedAsync(requests);
        await bot.Api.CreateClient().SetWebhook(bot.Api.WebhookUrl!, secretToken: "s3cret");

        // Act
        await bot.PrivateChat("Nick").SendsAsync("hello");

        // Assert
        requests
            .Seen.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new SeenRequest("bot.example.com", "https", "s3cret", 1));
    }

    [Fact]
    public async Task Updates_ShouldBeNumberedInTheOrderTheyArrive()
    {
        // Arrange
        var requests = new WebhookRequests();
        await using var bot = await StartWatchedAsync(requests);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("one");
        await nick.TapsAsync("Again");
        await nick.SendsAsync("two");

        // Assert
        requests.Seen.Select(x => x.UpdateId).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Update_ToAnAppThatAllowsOnlyItsOwnHost_ShouldGetThrough()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(
            BotMode.Webhook,
            configure: web =>
                web.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?> { ["AllowedHosts"] = "bot.example.com" }
                        )
                )
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.LastMessage.Text.Should().Be("Reply: hello");
    }

    [Fact]
    public async Task SendsAsync_CancelledWhileTheBotWorks_ShouldLeaveTheNextUpdateUnharmed()
    {
        // Arrange
        var requests = new WebhookRequests { Delay = TimeSpan.FromMilliseconds(300) };
        await using var bot = await StartWatchedAsync(requests);
        var nick = bot.PrivateChat("Nick");
        using var impatient = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await ((Func<Task>)(() => nick.SendsAsync("slow", impatient.Token)))
            .Should()
            .ThrowAsync<OperationCanceledException>();
        await requests.FirstFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text).Should().Equal("Reply: slow", "Reply: hello");
    }

    [Fact]
    public async Task DisposeAsync_ShouldLetAnUpdateTheTestGaveUpOnFinishFirst()
    {
        // Arrange
        var requests = new WebhookRequests { Delay = TimeSpan.FromMilliseconds(500) };
        var bot = await StartWatchedAsync(requests);
        using var impatient = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await ((Func<Task>)(() => bot.PrivateChat("Nick").SendsAsync("slow", impatient.Token)))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        // Act
        await bot.DisposeAsync();

        // Assert
        requests.FirstFinished.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Updates_ShouldCarryNoCookieTheAppHandedOut()
    {
        // Arrange
        var requests = new WebhookRequests { SetsCookie = true };
        await using var bot = await StartWatchedAsync(requests);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("one");
        await nick.SendsAsync("two");

        // Assert
        requests.Seen.Select(x => x.HadCookie).Should().Equal(false, false);
    }

    [Fact]
    public async Task Update_WhenTelegramRefusedTheWebhook_ShouldSayThereIsNowhereToPostIt()
    {
        // Arrange: Telegram refuses an http webhook.
        await using var bot = await SandboxBot.StartAsync(BotMode.Webhook, baseUrl: "http://bot.example.com");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*set no webhook*");
    }

    [Fact]
    public async Task Update_PostedWhereNoEndpointListens_ShouldSayTheEndpointIsMissing()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(BotMode.Webhook);
        await bot.Api.CreateClient().SetWebhook($"{SandboxBot.BaseUrl}/elsewhere");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*/elsewhere with 404*Is the update endpoint mapped there*");
    }

    [Fact]
    public async Task Update_PostedWhereOnlyGetIsMapped_ShouldSayTheEndpointIsMissing()
    {
        // Arrange: the sandbox maps only GET /.
        await using var bot = await SandboxBot.StartAsync(BotMode.Webhook);
        await bot.Api.CreateClient().SetWebhook($"{SandboxBot.BaseUrl}/");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*with 405*Is the update endpoint mapped there*");
    }

    [Fact]
    public async Task Update_WhenTheAppRedirects_ShouldSayTelegramFollowsNoRedirect()
    {
        // Arrange
        var requests = new WebhookRequests { RedirectTo = "/login" };
        await using var bot = await StartWatchedAsync(requests);
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*with 302*redirects to /login*follows no redirect*");
            requests.Seen.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Update_WhenTheAppFails_ShouldSayWhatItAnswered()
    {
        // Arrange: the update handler cannot be built.
        await using var bot = await SandboxBot.StartAsync(
            BotMode.Webhook,
            configure: web =>
                web.ConfigureTestServices(services =>
                    services.AddScoped<IUpdateHandler>(_ => throw new InvalidOperationException("The database is down"))
                )
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            var failure = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
            failure.Should().Contain("with 500").And.Contain("The database is down");
            failure.Should().NotContain("Is the update endpoint mapped there");
        }
    }

    [Fact]
    public async Task Update_WithTheConfiguredSecretToken_ShouldBeHandled()
    {
        // Arrange
        await using var bot = await StartWithSecretTokenAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Reply: hello");
            bot.Api.Calls.Last(x => x.Method == "setWebhook").Parameters["secret_token"]!
                .GetValue<string>()
                .Should()
                .Be("s3cret");
        }
    }

    [Fact]
    public async Task Update_WithAWrongSecretToken_ShouldBeRefusedWith401()
    {
        // Arrange
        await using var bot = await StartWithSecretTokenAsync();
        await bot.Api.CreateClient().SetWebhook(bot.Api.WebhookUrl!, secretToken: "other");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*with 401*");
            nick.Messages.Where(x => x.IsFromBot).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Update_WithoutTheSecretToken_ShouldBeRefusedWith401()
    {
        // Arrange
        await using var bot = await StartWithSecretTokenAsync();
        await bot.Api.CreateClient().SetWebhook(bot.Api.WebhookUrl!);
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*with 401*");
    }

    [Fact]
    public async Task Startup_WithAnInvalidSecretToken_ShouldFail()
    {
        // Act
        var act = () =>
            SandboxBot.StartAsync(
                BotMode.Webhook,
                configure: web => web.UseSetting("Telegram:SecretToken", "my secret!")
            );

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("*SecretToken must be 1-256 characters of A-Z, a-z, 0-9, _ and -*");
    }

    [Fact]
    public async Task ForWebhookAsync_WhenTheAppPolls_ShouldSayToStartItWithLongPolling()
    {
        // Arrange: without a base URL the app polls.
        await using var bot = await TelegramTestHost.ForWebhookAsync<Program>(web =>
            SandboxBot.Configure(web, BotMode.LongPolling)
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "The app polls for updates instead of setting a webhook; start it with ForLongPollingAsync<Program>."
            );
    }

    private static Task<TelegramTestHost> StartWithSecretTokenAsync() =>
        SandboxBot.StartAsync(BotMode.Webhook, configure: web => web.UseSetting("Telegram:SecretToken", "s3cret"));

    private static Task<TelegramTestHost> StartWatchedAsync(WebhookRequests requests) =>
        SandboxBot.StartAsync(
            BotMode.Webhook,
            configure: web => web.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(requests))
        );

    private sealed record SeenRequest(
        string Host,
        string Scheme,
        string? SecretToken,
        long UpdateId,
        bool HadCookie = false
    );

    // Records every request ahead of the app's pipeline, and can delay, redirect or set a cookie on it.
    private sealed class WebhookRequests : IStartupFilter
    {
        private readonly List<SeenRequest> _seen = [];

        public TimeSpan Delay { get; init; }

        public string? RedirectTo { get; init; }

        public bool SetsCookie { get; init; }

        public TaskCompletionSource FirstFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SeenRequest> Seen
        {
            get
            {
                lock (_seen)
                {
                    return [.. _seen];
                }
            }
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    async (context, proceed) =>
                    {
                        context.Request.EnableBuffering();
                        var update = await JsonNode.ParseAsync(context.Request.Body);
                        context.Request.Body.Position = 0;

                        lock (_seen)
                        {
                            _seen.Add(
                                new SeenRequest(
                                    context.Request.Host.Value!,
                                    context.Request.Scheme,
                                    context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault(),
                                    update!["update_id"]!.GetValue<long>(),
                                    context.Request.Headers.Cookie.Count > 0
                                )
                            );
                        }

                        if (SetsCookie)
                        {
                            context.Response.Cookies.Append("session", "1");
                        }

                        if (RedirectTo is not null)
                        {
                            context.Response.Redirect(RedirectTo);
                            return;
                        }

                        // A slow app that does not notice the caller giving up.
                        await Task.Delay(Delay, CancellationToken.None);
                        await proceed();
                        FirstFinished.TrySetResult();
                    }
                );
                next(app);
            };
    }
}
