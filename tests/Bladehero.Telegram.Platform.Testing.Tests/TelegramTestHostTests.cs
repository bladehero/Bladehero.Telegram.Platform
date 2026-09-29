using System.Diagnostics;
using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Errors;
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
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom from Nick");
    }

    [Fact]
    public async Task SendAsync_ByTwoUsersAtOnce_ShouldRethrowOnlyTheSendersError()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var anna = bot.PrivateChat("Anna");

        // Act
        var rounds = await RoundsAtOnceAsync(() => nick.SendsAsync("/boom"), () => anna.SendsAsync("/whoami"));

        // Assert
        rounds.Should().AllBeEquivalentTo("boom from Nick | no error");
    }

    [Fact]
    public async Task SendAsync_BothFailingAtOnce_ShouldEachRethrowTheirOwnError()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        var anna = bot.PrivateChat("Anna");

        // Act
        var rounds = await RoundsAtOnceAsync(() => nick.SendsAsync("/boom"), () => anna.SendsAsync("/boom"));

        // Assert
        rounds.Should().AllBeEquivalentTo("boom from Nick | boom from Anna");
    }

    [Fact]
    public async Task SendAsync_AfterGivingUpOnAFailingUpdate_ShouldNotRethrowItsErrorLater()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hi");
        using var impatient = new CancellationTokenSource();
        var givenUp = nick.SendsAsync("/slowboom", impatient.Token);
        await impatient.CancelAsync();
        await ((Func<Task>)(() => givenUp)).Should().ThrowAsync<OperationCanceledException>();

        // Act: the bot takes this update only once it has finished the one given up on.
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WhenACommandThrows_ShouldAlsoRunTheAppsOwnErrorHandler()
    {
        // Arrange
        var seen = new SeenErrors();
        await using var bot = await TestBot.StartAsync(services: services =>
        {
            services.AddSingleton(seen);
            services.AddScoped<ITelegramErrorHandler, SeenErrorsHandler>();
        });
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom from Nick");
            var error = seen.All.Should().ContainSingle().Subject;
            error.Exception.Should().BeSameAs(thrown);
            error.Update!.Id.Should().Be(1);
            error.Update.Message!.Chat.Id.Should().Be(nick.Chat.Id);
        }
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

    [Theory]
    [InlineData("instance")]
    [InlineData("factory")]
    [InlineData("concrete type")]
    [InlineData("typed HttpClient")]
    public async Task ForLongPollingAsync_WhenTheAppRegistersItsOwnBotClient_ShouldPointItAtTheFake(string registration)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services => RegisterOwnClient(services, registration));
        var nick = bot.PrivateChat("Nick");
        var client =
            registration == "concrete type"
                ? bot.Services.GetRequiredService<TelegramBotClient>()
                : bot.Services.GetRequiredService<ITelegramBotClient>();

        // Act
        await client.SendMessage(nick.Chat.Id, "Your limit is near");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: Your limit is near");
            bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WhenTheAppRegistersNoBotClient_ShouldNotAddOne()
    {
        // Act
        await using var bot = await TestBot.StartAsync();

        // Assert
        using (new AssertionScope())
        {
            bot.Services.GetService<ITelegramBotClient>().Should().BeNull();
            bot.Services.GetService<TelegramBotClient>().Should().BeNull();
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

    // Twenty rounds of both actions at once, each read as "first's outcome | second's outcome".
    private static async Task<List<string>> RoundsAtOnceAsync(Func<Task> first, Func<Task> second)
    {
        var rounds = new List<string>();
        for (var round = 0; round < 20; round++)
        {
            rounds.Add(string.Join(" | ", await Task.WhenAll(OutcomeOf(first()), OutcomeOf(second()))));
        }

        return rounds;
    }

    private static async Task<string> OutcomeOf(Task action)
    {
        try
        {
            await action;
            return "no error";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static void RegisterOwnClient(IServiceCollection services, string registration)
    {
        const string realLookingToken = "7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

        switch (registration)
        {
            case "instance":
                services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(realLookingToken));
                break;
            case "factory":
                services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(realLookingToken));
                break;
            case "concrete type":
                services.AddSingleton(new TelegramBotClient(realLookingToken));
                break;
            default:
                services
                    .AddHttpClient("telegram")
                    .AddTypedClient<ITelegramBotClient>(http => new TelegramBotClient(realLookingToken, http));
                break;
        }
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

    // The app's own error handler, and what it saw.
    private sealed class SeenErrors
    {
        private readonly List<TelegramError> _all = [];

        public IReadOnlyList<TelegramError> All
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Add(TelegramError error)
        {
            lock (_all)
            {
                _all.Add(error);
            }
        }
    }

    private sealed class SeenErrorsHandler(SeenErrors seen) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            seen.Add(telegramError);
            return Task.CompletedTask;
        }
    }
}
