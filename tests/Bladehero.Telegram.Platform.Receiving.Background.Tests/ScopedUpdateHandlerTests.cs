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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleErrorAsync(
                Client,
                new InvalidOperationException("original"),
                HandleErrorSource.HandleUpdateError,
                CancellationToken.None
            )
        );

        Assert.NotEmpty(log.Disposed);
        Assert.Equal(log.Created, log.Disposed);
    }

    private static ServiceProvider BuildProvider(ScopeLog log, bool throwing = false)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
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

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
