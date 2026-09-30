using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TelegramTestHostTests
{
    private const string Probe = "Bladehero.Telegram.Platform.Testing.Tests.TestBot.ProbeCommand";

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
        // Arrange: publishing the command menu fails at startup, which the library logs as an error.
        var api = new FakeBotApi();
        api.Fail("setMyCommands", new BotApiError(500, "Internal Server Error"));
        await using var bot = await TestBot.StartAsync(api);
        bot.FailOnErrorLogs = true;
        var nick = bot.PrivateChat("Nick");

        // Act
        var failure = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));
        var next = () => nick.SendsAsync("hello again");

        // Assert
        var logged = failure.Should().BeOfType<InvalidOperationException>().Subject;
        using (new AssertionScope())
        {
            logged
                .Message.Should()
                .Be(
                    "The bot logged an error outside any update: Error "
                        + "[Bladehero.Telegram.Platform.Receiving.Background.TelegramCommandMenuInitializer] Failed to "
                        + "update the command menu (ApiRequestException)"
                );
            logged.InnerException.Should().BeOfType<ApiRequestException>();
            await next.Should().NotThrowAsync();
        }
    }
}
