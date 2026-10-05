using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class TelegramLockStopTests
{
    // Only bounds a failing test's wait; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StoppingTheHostCancelsWorkStillWaitingForTheLock()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddTelegramLock();
        builder.Services.AddHostedService<CarelessWorker>();
        using var host = builder.Build();
        var telegramLock = host.Services.GetRequiredService<ITelegramLock>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = telegramLock.RunAsync((_, _) => release.Task);
        await host.StartAsync();
        await telegramLock.WaitForWaitersAsync(1);

        await host.StopAsync().WaitAsync(Patience);

        var worker = host.Services.GetServices<IHostedService>().OfType<CarelessWorker>().Single();
        Assert.True(worker.ExecuteTask!.IsCanceled);
        Assert.False(worker.Ran);
        Assert.Equal(0, telegramLock.WaitingCount);
        release.SetResult();
        await holder;
    }

    // Waits for the lock without its stopping token, so only the lock's own link to the host's stop ends the wait.
    private sealed class CarelessWorker(ITelegramLock telegramLock) : BackgroundService
    {
        public bool Ran { get; private set; }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            telegramLock.RunAsync(
                (_, _) =>
                {
                    Ran = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None
            );
    }
}
