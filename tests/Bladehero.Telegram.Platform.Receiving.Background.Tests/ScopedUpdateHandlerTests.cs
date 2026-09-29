using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class ScopedUpdateHandlerTests
{
    private static readonly ITelegramBotClient Client = new TelegramBotClient(
        "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"
    );

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
        Assert.Equal("boom", logged.Exception?.Message);
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
        await using var provider = BuildProvider(log);
        var handler = provider.GetRequiredService<ScopedUpdateHandler>();
        var failure = new HttpRequestException("Telegram is unreachable");

        await handler.HandleErrorAsync(Client, failure, HandleErrorSource.PollingError, CancellationToken.None);

        var error = Assert.Single(log.Errors);
        Assert.Same(failure, error.Exception);
        Assert.Null(error.Update);
    }

    private static ServiceProvider BuildProvider(
        ScopeLog log,
        bool throwing = false,
        LogRecorder? logs = null,
        Action<IServiceCollection>? configure = null
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
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
