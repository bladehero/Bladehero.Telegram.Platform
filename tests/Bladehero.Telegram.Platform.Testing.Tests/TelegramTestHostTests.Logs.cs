using Bladehero.Telegram.Platform.Receiving.Errors;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TelegramTestHostTests
{
    private const string Probe = "Bladehero.Telegram.Platform.Testing.Tests.TestBot.ProbeCommand";
    private const string LateFailure = "Bladehero.Telegram.Platform.Testing.Tests.TestBot.LateFailureCommand";
    private const string Apology =
        "Bladehero.Telegram.Platform.Testing.Tests.TelegramTestHostTests.ApologizingErrorHandler";

    [Fact]
    public async Task Logs_ShouldHoldWhatTheBotLoggedWithItsUpdate()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/probe");

        // Assert
        var entry = bot.Logs.Should().ContainSingle(x => x.Category == Probe).Subject;
        using (new AssertionScope())
        {
            entry.ToString().Should().Be($"Debug [{Probe}] Probed by Nick");
            entry.UpdateId.Should().Be(1);
            entry.Exception.Should().BeNull();
        }
    }

    [Fact]
    public async Task Logs_ShouldHoldStartupLogs()
    {
        // Act
        await using var bot = await TestBot.StartAsync();

        // Assert
        bot.Logs.Should()
            .ContainSingle(x => x.Message.StartsWith("Command menu"))
            .Which.Should()
            .Be(
                new TestLog(
                    LogLevel.Information,
                    "Bladehero.Telegram.Platform.Receiving.Background.TelegramCommandMenuInitializer",
                    0,
                    "Command menu updated: 2 commands",
                    Exception: null,
                    UpdateId: null
                )
            );
    }

    [Fact]
    public async Task Logs_WhenTheAppFiltersToWarning_ShouldStillHoldDebugEntries()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddLogging(logging =>
                logging.SetMinimumLevel(LogLevel.Warning).AddFilter("Bladehero", LogLevel.Warning)
            )
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/probe");

        // Assert
        bot.Logs.Should().ContainSingle(x => x.Category == Probe).Which.Level.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task Logs_WhenTheAppClearsItsProviders_ShouldStillBeCaptured()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddLogging(logging => logging.ClearProviders())
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/probe");

        // Assert
        bot.Logs.Should().ContainSingle(x => x.Category == Probe);
    }

    [Fact]
    public async Task FailOnErrorLogs_WhenOff_ShouldLetAnErrorLogPass()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("/probe Error");

        // Assert
        using (new AssertionScope())
        {
            await act.Should().NotThrowAsync();
            bot.Logs.Should().ContainSingle(x => x.Category == Probe).Which.Level.Should().Be(LogLevel.Error);
        }
    }

    [Theory]
    [InlineData("/probe Error", "")]
    [InlineData("/probe Error Critical", " (and 1 more)")]
    public async Task FailOnErrorLogs_WhenOn_ShouldFailTheActionWhoseUpdateLoggedAnError(string probe, string more)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");

        // Act
        var failure = await Record.ExceptionAsync(() => nick.SendsAsync(probe));

        // Assert
        var logged = failure.Should().BeOfType<InvalidOperationException>().Subject;
        using (new AssertionScope())
        {
            logged
                .Message.Should()
                .Be(
                    $"The bot logged an error while handling update 1: Error [{Probe}] Probed by Nick "
                        + $"(InvalidOperationException){more}"
                );
            logged.InnerException!.Message.Should().Be("The ledger is off");
        }
    }

    [Fact]
    public async Task FailOnErrorLogs_WhenOn_ShouldNotReportAThrownErrorTwice()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));
        var next = () => nick.SendsAsync("hello");

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom from Nick");
            await next.Should().NotThrowAsync();
            bot.Logs.Should().Contain(x => x.Level == LogLevel.Error && x.Exception == thrown);
        }
    }

    [Fact]
    public async Task FailOnErrorLogs_WhenOn_ShouldFailTheNextActionForAnErrorLoggedOutsideAnyUpdate()
    {
        // Arrange: a background job of the app logs an error, outside any update.
        await using var bot = await TestBot.StartAsync();
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");
        bot.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Nightly")
            .LogError(new TimeoutException("The bank timed out"), "Import failed");

        // Act
        var failure = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));
        var next = () => nick.SendsAsync("hello again");

        // Assert
        var logged = failure.Should().BeOfType<InvalidOperationException>().Subject;
        using (new AssertionScope())
        {
            logged
                .Message.Should()
                .Be("The bot logged an error outside any update: Error [Nightly] Import failed (TimeoutException)");
            logged.InnerException.Should().BeOfType<TimeoutException>();
            await next.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Logs_FromTheAppsErrorHandler_ShouldCarryTheUpdateId()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddScoped<ITelegramErrorHandler, ApologizingErrorHandler>()
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));

        // Assert
        bot.Logs.Should().ContainSingle(x => x.Category == Apology).Which.UpdateId.Should().Be(1);
    }

    [Fact]
    public async Task FailOnErrorLogs_WhenTheErrorHandlerLogsAnError_ShouldNotFailTheNextAction()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: services =>
            services.AddScoped<ITelegramErrorHandler, ApologizingErrorHandler>()
        );
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("/boom"));
        var next = () => nick.SendsAsync("/ping");

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom from Nick");
            await next.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task FailOnErrorLogs_ForAnErrorLoggedAfterItsActionReturned_ShouldFailTheNextAction()
    {
        // Arrange: /latefail logs from work it doesn't wait for, once let go after its action returned.
        var late = new TestBot.LateWork();
        await using var bot = await TestBot.StartAsync(services: services => services.AddSingleton(late));
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/latefail");
        late.Go.SetResult();
        await late.Done.Task;

        // Act
        var failure = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));

        // Assert
        failure
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Be(
                "The bot logged an error by work update 1 started, after its action returned: "
                    + $"Error [{LateFailure}] Late failure for Nick"
            );
    }

    [Fact]
    public async Task FailOnErrorLogs_TurnedOnAfterAnErrorWasLogged_ShouldNotFailTheNextAction()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/probe Error");

        // Act
        bot.FailOnErrorLogs = true;
        var next = () => nick.SendsAsync("/probe");

        // Assert
        await next.Should().NotThrowAsync();
    }

    // Logs an error of its own, without the exception.
    private sealed class ApologizingErrorHandler(ILogger<ApologizingErrorHandler> logger) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            logger.LogError("Apologised for update {UpdateId}", telegramError.Update?.Id);
            return Task.CompletedTask;
        }
    }
}
