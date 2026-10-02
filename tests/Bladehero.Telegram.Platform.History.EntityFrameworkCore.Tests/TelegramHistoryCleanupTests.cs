using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests.EntityFrameworkCoreTelegramHistoryStoreTests;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

public sealed class TelegramHistoryCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // Only bounds a failing test's wait for a pass; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CleanUpAsync_ShouldDeleteEntriesOlderThanMaxAgeOnly()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(
            database,
            Entry("a month and a day ago", time: Now.AddDays(-31)),
            Entry("29 days ago", time: Now.AddDays(-29)),
            Entry("now", time: Now)
        );
        await using var provider = database.AddTo(new ServiceCollection()).BuildServiceProvider();
        var sut = new TelegramHistoryCleanup<BudgetContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromDays(30),
            NullLogger<TelegramHistoryCleanup<BudgetContext>>.Instance,
            new FakeTimeProvider(Now)
        );

        // Act
        var deleted = await sut.CleanUpAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().Be(1);
            (await ReadAsync(database, new())).Select(x => x.Text).Should().Equal("29 days ago", "now");
        }
    }

    [Fact]
    public async Task Cleanup_ShouldRunAgainEveryHour()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(database, Entry("23.5 hours ago", time: Now.AddHours(-23.5)), Entry("now", time: Now));
        var time = new FakeTimeProvider(Now);
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddSingleton<TimeProvider>(time);
        database.AddTo(builder.Services);
        builder.Services.AddTelegramHistory().UseEntityFrameworkCore<BudgetContext>(maxAge: TimeSpan.FromDays(1));
        using var host = builder.Build();
        await host.StartAsync();
        var cleanup = host
            .Services.GetServices<IHostedService>()
            .OfType<TelegramHistoryCleanup<BudgetContext>>()
            .Single();
        await cleanup.WaitForPassesAsync(1, CancellationToken.None).WaitAsync(Patience);
        var afterTheFirstPass = await ReadAsync(database, new());

        // Act
        time.Advance(TimeSpan.FromHours(1));
        await cleanup.WaitForPassesAsync(2, CancellationToken.None).WaitAsync(Patience);

        // Assert
        using (new AssertionScope())
        {
            afterTheFirstPass.Should().HaveCount(2);
            (await ReadAsync(database, new())).Select(x => x.Text).Should().Equal("now");
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task Cleanup_ShouldWaitForTheHostToStart()
    {
        // Arrange
        await using var database = TestDatabase.Unmigrated();
        var logs = new LogRecorder();
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Logging.AddProvider(logs);
        database.AddTo(builder.Services);
        builder.Services.AddTelegramHistory().UseEntityFrameworkCore<BudgetContext>(maxAge: TimeSpan.FromDays(1));
        builder.Services.AddHostedService<Migration>();
        using var host = builder.Build();

        // Act
        await host.StartAsync();
        var cleanup = host
            .Services.GetServices<IHostedService>()
            .OfType<TelegramHistoryCleanup<BudgetContext>>()
            .Single();
        await cleanup.WaitForPassesAsync(1, CancellationToken.None).WaitAsync(Patience);

        // Assert
        logs.Entries.Should().NotContain(x => x.Level >= LogLevel.Error);
        await host.StopAsync();
    }

    // Creates the database as it starts, after the cleanup, as an app's own migration may.
    private sealed class Migration(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BudgetContext>().Database.MigrateAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
