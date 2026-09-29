using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class ScopedUpdateHandlerTests
{
    private static readonly ITelegramBotClient Client = new TelegramBotClient(
        "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"
    );

    // Only bounds a failing test's wait for the handler to let go once the clock has moved on; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task EveryUpdateIsHandledInItsOwnScope()
    {
        var log = new ScopeLog();
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 1 }, CancellationToken.None);
        await handler.HandleUpdateAsync(Client, new Update { Id = 2 }, CancellationToken.None);

        Assert.Equal(2, log.SeenByUpdates.Count);
        Assert.NotSame(log.SeenByUpdates[0], log.SeenByUpdates[1]);
    }

    [Fact]
    public async Task CommandsAreResolvedFreshForEveryUpdate()
    {
        var log = new ScopeLog();
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 1 }, CancellationToken.None);
        await handler.HandleUpdateAsync(Client, new Update { Id = 2 }, CancellationToken.None);

        Assert.Equal(2, log.CommandInstances.Count);
        Assert.NotSame(log.CommandInstances[0], log.CommandInstances[1]);
    }

    [Fact]
    public async Task TheScopeIsDisposedWhenTheUpdateCompletes()
    {
        var log = new ScopeLog();
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 1 }, CancellationToken.None);

        Assert.NotEmpty(log.Disposed);
        Assert.Equal(log.Created, log.Disposed);
    }

    [Fact]
    public async Task EveryErrorIsHandledInItsOwnScope()
    {
        var log = new ScopeLog();
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleErrorAsync(
            Client,
            new InvalidOperationException("first"),
            HandleErrorSource.HandleUpdateError,
            CancellationToken.None
        );
        await handler.HandleErrorAsync(
            Client,
            new InvalidOperationException("second"),
            HandleErrorSource.HandleUpdateError,
            CancellationToken.None
        );

        Assert.Equal(2, log.SeenByErrors.Count);
        Assert.NotSame(log.SeenByErrors[0], log.SeenByErrors[1]);
    }

    [Fact]
    public async Task AThrowingHandlerStillDisposesItsScope()
    {
        var log = new ScopeLog();
        await using var provider = BuildProvider(log, throwing: true);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleErrorAsync(
            Client,
            new InvalidOperationException("original"),
            HandleErrorSource.HandleUpdateError,
            CancellationToken.None
        );

        Assert.NotEmpty(log.Disposed);
        Assert.Equal(log.Created, log.Disposed);
    }

    [Fact]
    public async Task AFailingCommandIsReportedWithItsUpdate()
    {
        var log = new ScopeLog { CommandFailure = new InvalidOperationException("The database is down") };
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 7 }, CancellationToken.None);

        var error = Assert.Single(log.Errors);
        Assert.Same(log.CommandFailure, error.Exception);
        Assert.Equal(7, error.Update?.Id);
    }

    [Fact]
    public async Task AStrayCancellationFromACommandIsReportedInsteadOfEndingPolling()
    {
        var log = new ScopeLog { CommandFailure = new TaskCanceledException("The HTTP call timed out") };
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 7 }, CancellationToken.None);

        var error = Assert.Single(log.Errors);
        Assert.Same(log.CommandFailure, error.Exception);
        Assert.Equal(7, error.Update?.Id);
    }

    [Fact]
    public async Task AnUpdateHandlerThatCannotBeBuiltIsReportedWithTheUpdate()
    {
        var log = new ScopeLog();
        var failure = new InvalidOperationException("The database is down");
        await using var provider = BuildProvider(
            log,
            configure: services => services.AddScoped<IUpdateHandler>(_ => throw failure)
        );
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await handler.HandleUpdateAsync(Client, new Update { Id = 7 }, CancellationToken.None);

        var error = Assert.Single(log.Errors);
        Assert.Same(failure, error.Exception);
        Assert.Equal(7, error.Update?.Id);
    }

    [Fact]
    public async Task AFailingErrorHandlerIsLoggedAndSwallowed()
    {
        var log = new ScopeLog { CommandFailure = new InvalidOperationException("original") };
        var logs = new LogRecorder();
        await using var provider = BuildProvider(log, throwing: true, logs: logs);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        var escaped = await Record.ExceptionAsync(() =>
            handler.HandleUpdateAsync(Client, new Update { Id = 7 }, CancellationToken.None)
        );

        Assert.Null(escaped);
        var logged = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        var both = Assert.IsType<AggregateException>(logged.Exception);
        Assert.Collection(
            both.InnerExceptions,
            error => Assert.Same(log.CommandFailure, error),
            failure => Assert.Equal("boom", failure.Message)
        );
    }

    [Fact]
    public async Task ShutdownIsNotReportedAsAnError()
    {
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        var log = new ScopeLog { CommandFailure = new OperationCanceledException(shutdown.Token) };
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleUpdateAsync(Client, new Update { Id = 7 }, shutdown.Token)
        );

        Assert.Empty(log.Errors);
    }

    [Fact]
    public async Task APollingErrorIsReportedWithoutAnUpdate()
    {
        var log = new ScopeLog();
        var time = new FakeTimeProvider();
        await using var provider = BuildProvider(log, time: time);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        var failure = new HttpRequestException("Telegram is unreachable");

        var polled = handler.HandleErrorAsync(Client, failure, HandleErrorSource.PollingError, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await polled.WaitAsync(Patience);

        var error = Assert.Single(log.Errors);
        Assert.Same(failure, error.Exception);
        Assert.Null(error.Update);
    }

    [Fact]
    public async Task APollingErrorWaitsASecondBeforeTheNextPoll()
    {
        var time = new RecordingTimeProvider();
        await using var provider = BuildProvider(new ScopeLog(), time: time);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        var polled = PollingErrorAsync(handler);
        time.Advance(TimeSpan.FromMilliseconds(999));
        var waitingBefore = !polled.IsCompleted;
        time.Advance(TimeSpan.FromMilliseconds(1));
        await polled.WaitAsync(Patience);

        Assert.Equal([TimeSpan.FromSeconds(1)], time.DueTimes);
        Assert.True(waitingBefore);
    }

    [Fact]
    public async Task ConsecutivePollingErrorsWaitLongerUpToThirtySeconds()
    {
        var time = new RecordingTimeProvider();
        await using var provider = BuildProvider(new ScopeLog(), time: time);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();

        for (var error = 0; error < 7; error++)
        {
            await WaitOutAPollingErrorAsync(handler, time);
        }

        Assert.Equal([1, 2, 4, 8, 16, 30, 30], time.DueTimes.Select(wait => wait.TotalSeconds));
    }

    [Fact]
    public async Task AnUpdateResetsTheWait()
    {
        var time = new RecordingTimeProvider();
        await using var provider = BuildProvider(new ScopeLog(), time: time);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        await WaitOutAPollingErrorAsync(handler, time);
        await WaitOutAPollingErrorAsync(handler, time);

        await handler.HandleUpdateAsync(Client, new Update { Id = 1 }, CancellationToken.None);
        await WaitOutAPollingErrorAsync(handler, time);

        Assert.Equal([1, 2, 1], time.DueTimes.Select(wait => wait.TotalSeconds));
    }

    [Fact]
    public async Task AQuietMinuteResetsTheWait()
    {
        var time = new RecordingTimeProvider();
        await using var provider = BuildProvider(new ScopeLog(), time: time);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        await WaitOutAPollingErrorAsync(handler, time);
        await WaitOutAPollingErrorAsync(handler, time);

        time.Advance(TimeSpan.FromMinutes(1));
        await WaitOutAPollingErrorAsync(handler, time);

        Assert.Equal([1, 2, 1], time.DueTimes.Select(wait => wait.TotalSeconds));
    }

    [Fact]
    public async Task WithoutATimeProviderPollingBacksOffOnTheSystemClock()
    {
        // Built and validated with no TimeProvider registered, as the library registers none.
        await using var provider = BuildProvider(new ScopeLog(), registersAClock: false);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        using var shutdown = new CancellationTokenSource();

        var polled = handler.HandleErrorAsync(
            Client,
            new HttpRequestException("Telegram is unreachable"),
            HandleErrorSource.PollingError,
            shutdown.Token
        );
        var waiting = !polled.IsCompleted;
        await shutdown.CancelAsync();
        await polled.WaitAsync(Patience);

        // The second-long wait was scheduled on the real clock; the shutdown ended it rather than time passing.
        Assert.True(waiting);
    }

    [Fact]
    public async Task ShutdownEndsTheWaitAtOnce()
    {
        await using var provider = BuildProvider(new ScopeLog(), time: new RecordingTimeProvider());
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        using var shutdown = new CancellationTokenSource();
        var polled = handler.HandleErrorAsync(
            Client,
            new HttpRequestException("Telegram is unreachable"),
            HandleErrorSource.PollingError,
            shutdown.Token
        );

        await shutdown.CancelAsync();

        // The clock never moved, so only the shutdown can end the wait, and without an exception.
        await polled.WaitAsync(Patience);
    }

    private static Task PollingErrorAsync(ScopedUpdateHandler handler) =>
        handler.HandleErrorAsync(
            Client,
            new HttpRequestException("Telegram is unreachable"),
            HandleErrorSource.PollingError,
            CancellationToken.None
        );

    // A polling error, then the clock moved on by as long as the handler asked to wait.
    private static async Task WaitOutAPollingErrorAsync(ScopedUpdateHandler handler, RecordingTimeProvider time)
    {
        var polled = PollingErrorAsync(handler);
        time.Advance(time.DueTimes[^1]);
        await polled.WaitAsync(Patience);
    }

    private static ServiceProvider BuildProvider(
        ScopeLog log,
        bool throwing = false,
        LogRecorder? logs = null,
        Action<IServiceCollection>? configure = null,
        TimeProvider? time = null,
        bool registersAClock = true
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logs is null)
            {
                builder.SetMinimumLevel(LogLevel.None);
            }
            else
            {
                builder.AddProvider(logs);
            }
        });
        services.AddSingleton(log);
        if (registersAClock)
        {
            services.AddSingleton(time ?? new FakeTimeProvider());
        }

        services.AddScoped<ScopedDependency>();
        services.AddTelegramReceiving(typeof(ProbeCommand).Assembly);
        services.AddSingleton<ScopedUpdateHandler>();

        if (throwing)
        {
            services.AddScoped<ITelegramErrorHandler, ThrowingErrorHandler>();
        }
        else
        {
            services.AddScoped<ITelegramErrorHandler, ProbeErrorHandler>();
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }
}
