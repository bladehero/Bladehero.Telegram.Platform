using System.Diagnostics;
using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Background;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TelegramTestHostTests
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
    public async Task ForLongPollingAsync_WithASingletonErrorHandlerAHostedServiceUses_ShouldStartAndStillRecord()
    {
        // Arrange: a scoped recorder in its place would be a scoped service in a singleton, refused on build.
        var seen = new SeenErrors();
        await using var bot = await TestBot.StartAsync(services: services =>
        {
            services.AddSingleton(seen);
            services.AddSingleton<ITelegramErrorHandler, SeenErrorsHandler>();
            services.AddHostedService<ErrorReportingService>();
        });
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom from Nick");
            seen.All.Should().ContainSingle().Which.Exception.Should().BeSameAs(thrown);
        }
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
    public async Task SendAsync_WhenTheAppsErrorHandlerFails_ShouldRethrowBothErrors()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddScoped<ITelegramErrorHandler, FailingErrorHandler>()
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));

        // Assert
        using (new AssertionScope())
        {
            var both = thrown.Should().BeOfType<AggregateException>().Subject;
            both.Message.Should().StartWith("The app's ITelegramErrorHandler failed while handling update 1's error.");
            both.InnerExceptions.Select(x => x.Message).Should().Equal("boom from Nick", "The error log is full");
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

    [Fact]
    public async Task SendAsync_AChatMemberUpdate_WhenTheBotNeverAskedForIt_ShouldSayItMustBeAskedFor()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        var act = () => bot.SendAsync(Joined(family, "Anna"));

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage(
                    "Telegram sends chat_member only to a bot that asks for it: add UpdateType.ChatMember to "
                        + "AllowedUpdates."
                );
            family.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendAsync_AChatMemberUpdate_WhenTheBotAsksForIt_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(receiver: receiver =>
            receiver.AllowedUpdates = [UpdateType.Message, UpdateType.ChatMember]
        );
        var family = bot.GroupChat("Family");

        // Act
        await bot.SendAsync(Joined(family, "Anna"));

        // Assert
        family.LastMessage.ToString().Should().Be("Bot: Welcome, Anna");
    }

    [Fact]
    public async Task SendAsync_ARawTapWithAnAnsweredQueryId_ShouldBeAnsweredAgain()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;
        await nick.TapsAsync("A");

        // Act
        await bot.SendAsync(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "1",
                    From = new User { Id = nick.Id, FirstName = "Nick" },
                    Message = menu.Message,
                    ChatInstance = "1",
                    Data = "pick:B",
                },
            }
        );

        // Assert
        bot.Api.CallbackAnswer("1")!["text"]!
            .GetValue<string>()
            .Should()
            .Be("You picked B");
    }

    [Fact]
    public async Task SendMessage_ToAChatSeenInARawUpdate_ShouldBeAccepted()
    {
        // Arrange: no test chat opened chat 42, and no command answers /nothing.
        await using var bot = await TestBot.StartAsync();
        await bot.SendAsync(Text("/nothing"));

        // Act
        var sent = await bot.Api.CreateClient().SendMessage(42, "Welcome");

        // Assert
        using (new AssertionScope())
        {
            sent.Chat.Id.Should().Be(42);
            sent.Chat.Type.Should().Be(ChatType.Private);
        }
    }

    [Fact]
    public async Task PrivateChat_ForAGroupMember_ShouldLetTheBotWriteToThem()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.GroupChat("Family").Member("Anna");
        var inPrivate = bot.PrivateChat("Anna");

        // Act
        await bot.Api.CreateClient().SendMessage(anna.Id, "Your limit is near");

        // Assert
        inPrivate.LastMessage.ToString().Should().Be("Bot: Your limit is near");
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
    public async Task ForLongPollingAsync_ShouldPointTheBotsOwnClientAtTheFake()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await bot.Services.GetRequiredService<ITelegramBotClient>().SendMessage(nick.Chat.Id, "Your limit is near");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.ToString().Should().Be("Bot: Your limit is near");
            bot.Services.GetService<TelegramBotClient>().Should().BeNull();
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WithTheBuilder_ShouldBindTheBotFromConfiguration()
    {
        // Arrange
        const string token = "7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
        await using var bot = await TelegramTestHost.ForLongPollingAsync(builder =>
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["TelegramReceiverConfiguration:Token"] = token }
            );
            builder.Services.AddTelegramLongPollingReceiving(
                builder.Configuration,
                assemblies: typeof(TestBot).Assembly
            );
        });

        // Act
        await bot.SendAsync(Text("hello"));

        // Assert
        using (new AssertionScope())
        {
            bot.Services.GetRequiredService<IOptions<TelegramReceiverConfiguration>>().Value.Token.Should().Be(token);
            RepliesIn(bot.Api).Should().Equal("hello");
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WithTheBuilder_ShouldLetTheTestChooseTheEnvironment()
    {
        // Act
        await using var bot = await TelegramTestHost.ForLongPollingAsync(builder =>
        {
            builder.Environment.EnvironmentName = Environments.Production;
            builder.Services.AddTelegramLongPollingReceiving(
                receiver => receiver.Token = "unused",
                typeof(TestBot).Assembly
            );
        });

        // Assert
        bot.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.Should().Be(Environments.Production);
    }

    [Fact]
    public async Task ForLongPollingAsync_WithTheBuilder_ShouldHandTheBotsLogsToTheTest()
    {
        // Arrange
        var logs = new LogRecorder();
        await using var bot = await TelegramTestHost.ForLongPollingAsync(builder =>
        {
            builder.Logging.AddProvider(logs);
            builder.Services.AddTelegramLongPollingReceiving(
                receiver => receiver.Token = "unused",
                typeof(TestBot).Assembly
            );
        });

        // Act: the library's own error handler logs the failure.
        await Record.ExceptionAsync(() => bot.SendAsync(Text("/boom")));

        // Assert
        logs.Entries.Should().Contain(x => x.Level == LogLevel.Error && x.Exception!.Message == "boom from Nick");
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
    public async Task SendAsync_RightAfterStart_WithDropPendingUpdates_ShouldReachTheBot()
    {
        // Arrange
        var replies = new List<string>();

        // Act
        for (var round = 0; round < 20; round++)
        {
            await using var bot = await TestBot.StartAsync(receiver: receiver => receiver.DropPendingUpdates = true);
            await bot.SendAsync(Text("hello"));
            replies.Add(string.Join(", ", RepliesIn(bot.Api)));
        }

        // Assert
        replies.Should().AllBeEquivalentTo("hello");
    }

    [Fact]
    public async Task SendAsync_AfterARestartOnTheSameFake_ShouldReachTheNewBot()
    {
        // Arrange
        var api = new FakeBotApi();
        await using (
            var first = await TestBot.StartAsync(api, receiver: receiver => receiver.DropPendingUpdates = true)
        )
        {
            await first.SendAsync(Text("hello"));
        }

        await using var second = await TestBot.StartAsync(
            api,
            receiver: receiver => receiver.DropPendingUpdates = true
        );

        // Act
        await second.SendAsync(Text("hello again"));

        // Assert
        RepliesIn(api).Should().Equal("hello", "hello again");
    }

    [Fact]
    public async Task ForLongPollingAsync_WhileAnotherHostPollsTheSameFake_ShouldFailTheOlderOnesNextActionWithAConflict()
    {
        // Arrange: the newer host's first poll ends the older one's waiting poll.
        var api = new FakeBotApi();
        await using var older = await TestBot.StartAsync(api);
        await UntilAsync(() => api.PollWaiting);
        await using var newer = await TestBot.StartAsync(api);
        await UntilAsync(() => api.ConflictedPolling);

        // The older host records its 409 before it polls again.
        await api.PolledAsync(api.Polls).WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        var failure = await Record.ExceptionAsync(() => older.SendAsync(Text("hello")));

        // Assert
        var conflict = failure.Should().BeOfType<ApiRequestException>().Subject;
        using (new AssertionScope())
        {
            conflict.ErrorCode.Should().Be(409);
            conflict
                .Message.Should()
                .Be(
                    "Conflict: terminated by other getUpdates request; make sure that only one bot instance is running"
                );
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_AfterTheEarlierHostIsDisposed_ShouldNotConflict()
    {
        // Arrange
        var api = new FakeBotApi();
        await using (var earlier = await TestBot.StartAsync(api))
        {
            await earlier.SendAsync(Text("hello"));
        }

        await using var later = await TestBot.StartAsync(api);

        // Act
        await later.SendAsync(Text("hello again"));

        // Assert
        using (new AssertionScope())
        {
            RepliesIn(api).Should().Equal("hello", "hello again");
            api.ConflictedPolling.Should().BeFalse();
        }
    }

    [Fact]
    public async Task SendAsync_WhenACommandHangs_ShouldSayTheBotTookTheUpdateButDidNotFinish()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.SendAsync(Text("/hang"));

        // Assert
        await act.Should()
            .ThrowAsync<TimeoutException>()
            .WithMessage("The bot fetched update 1 but did not finish it within 1 second. A command may be hanging*");
    }

    [Fact]
    public async Task SendAsync_WithASubSecondTimeout_ShouldGiveItInMilliseconds()
    {
        // Arrange: nothing polls.
        await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramReceiving(typeof(TestBot).Assembly)
        );
        bot.UpdateTimeout = TimeSpan.FromMilliseconds(300);

        // Act
        var act = () => bot.SendAsync(Text("hello"));

        // Assert
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("No one fetched the update within 300 ms.*");
    }

    [Fact]
    public async Task DisposeAsync_WhenACommandHangs_ShouldNotWaitOutTheShutdownTimeout()
    {
        // Arrange
        var bot = await TestBot.StartAsync();
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);
        await ((Func<Task>)(() => bot.SendAsync(Text("/hang")))).Should().ThrowAsync<TimeoutException>();
        var stopwatch = Stopwatch.StartNew();

        // Act
        await bot.DisposeAsync();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the host's default shutdown timeout is 30 s");
    }

    [Fact]
    public async Task SendAsync_WhenTheHostStops_ShouldFailFastWithTheCause()
    {
        // Arrange
        var importer = new FailingImporter();
        await using var bot = await TestBot.StartAsync(services: services => services.AddHostedService(_ => importer));
        var waiting = bot.SendAsync(Text("/wait"));
        var stopwatch = Stopwatch.StartNew();
        var cause = new InvalidOperationException("The import queue is gone");

        // Act
        importer.Fail(cause);
        var failure = await Record.ExceptionAsync(() => waiting);

        // Assert
        using (new AssertionScope())
        {
            failure.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("host stopped");
            failure?.InnerException.Should().BeSameAs(cause);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the update timeout is 30 s");
        }
    }

    [Fact]
    public async Task ForLongPollingAsync_WhenStartupFails_ShouldStopPolling()
    {
        // Arrange
        var api = new FakeBotApi();

        // Act
        var act = () =>
            TestBot.StartAsync(api, services: services => services.AddHostedService(_ => new FailingStartup(api)));

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The cache could not warm up");
            api.PollsInFlight.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task UpdateTimeout_WhenZeroOrNegative_ShouldBeRefused(int milliseconds)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.UpdateTimeout = TimeSpan.FromMilliseconds(milliseconds);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
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

    // No timing: yields until the condition holds.
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static IEnumerable<string> RepliesIn(FakeBotApi api) =>
        api.Calls.Where(x => x.Method == "sendMessage").Select(x => x.Parameters["text"]!.GetValue<string>());

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

    private static Update Joined(TestChat group, string firstName)
    {
        var user = new User { Id = 2001, FirstName = firstName };
        return new Update
        {
            ChatMember = new ChatMemberUpdated
            {
                Chat = new Chat
                {
                    Id = group.Id,
                    Type = ChatType.Supergroup,
                    Title = group.ToString(),
                },
                From = user,
                Date = DateTime.UtcNow,
                OldChatMember = new ChatMemberLeft { User = user },
                NewChatMember = new ChatMemberMember { User = user },
            },
        };
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

    // A background service of the app's that fails when told to.
    private sealed class FailingImporter : BackgroundService
    {
        private readonly TaskCompletionSource _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Fail(Exception cause) => _failure.SetException(cause);

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => _failure.Task.WaitAsync(stoppingToken);
    }

    // Fails to start once the bot already polls, so there is a polling loop to leak.
    private sealed class FailingStartup(FakeBotApi api) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await api.PolledAsync(0).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            throw new InvalidOperationException("The cache could not warm up");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

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

    // A service of the app's that reports its own errors through the error handler.
    private sealed class ErrorReportingService(ITelegramErrorHandler errors) : IHostedService
    {
        public ITelegramErrorHandler Errors { get; } = errors;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingErrorHandler : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError) =>
            throw new InvalidOperationException("The error log is full");
    }
}
