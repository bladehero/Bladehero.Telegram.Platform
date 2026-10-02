using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class TelegramHistoryWriterTests
{
    [Fact]
    public async Task Record_ShouldStoreEntriesInTheOrderRecorded()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();

        // Act
        RecordEntries(host, 1, 2, 3);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        Texts(host.Store.Entries).Should().Equal("#1", "#2", "#3");
    }

    [Fact]
    public async Task Record_BeforeStart_ShouldWaitInTheQueueUntilStarted()
    {
        // Arrange
        await using var host = HistoryHost.Build();
        RecordEntries(host, 1);

        // Act
        await host.Host.StartAsync();
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        Texts(host.Store.Entries).Should().Equal("#1");
    }

    [Fact]
    public async Task Record_WhileTheStoreIsBusy_ShouldReturnAtOnce()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        await BusyStoreAsync(host);

        // Act
        var recorded = host.Writer.Record(HistoryHost.Entry(2));

        // Assert
        recorded.Should().BeTrue();
    }

    [Fact]
    public async Task Record_WhileTheStoreIsBusy_ShouldBatchWhatQueuedUpToAHundred()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        await BusyStoreAsync(host);
        RecordEntries(host, [.. Enumerable.Range(2, 250)]);

        // Act
        host.Store.Open();
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            host.Store.Batches.Select(x => x.Length).Should().Equal(1, 100, 100, 50);
            Texts(host.Store.Entries).Should().Equal(Enumerable.Range(1, 251).Select(x => $"#{x}"));
        }
    }

    [Fact]
    public async Task Record_WhenTheQueueIsFull_ShouldDropTheEntryAndWarnOnce()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.QueueCapacity = 2);
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3);

        // Act
        bool[] recorded = [host.Writer.Record(HistoryHost.Entry(4)), host.Writer.Record(HistoryHost.Entry(5))];
        host.Store.Open();
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            recorded.Should().Equal(false, false);
            Texts(host.Store.Entries).Should().Equal("#1", "#2", "#3");
            host.Logs.At(LogLevel.Warning).Should().Equal("The Telegram history queue is full (2 entries): 1 dropped.");
        }
    }

    [Fact]
    public async Task Record_WhenDropsGoOn_ShouldWarnAgainAfterAMinuteWithTheCount()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.QueueCapacity = 1);
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3, 4);
        host.Time.Advance(TimeSpan.FromSeconds(59));
        RecordEntries(host, 5);
        var withinTheMinute = host.Logs.At(LogLevel.Warning).Count;

        // Act
        host.Time.Advance(TimeSpan.FromSeconds(1));

        // Assert
        using (new AssertionScope())
        {
            withinTheMinute.Should().Be(1);
            host.Logs.At(LogLevel.Warning)
                .Should()
                .SatisfyRespectively(
                    first => first.Should().EndWith(": 1 dropped."),
                    second => second.Should().EndWith(": 2 dropped.")
                );
        }
    }

    [Fact]
    public async Task Record_WhenABurstIsDropped_ShouldReportTheRestAMinuteLater()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.QueueCapacity = 1);
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3, 4, 5);

        // Act
        host.Time.Advance(TimeSpan.FromMinutes(1));
        host.Time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        host.Logs.At(LogLevel.Warning)
            .Should()
            .SatisfyRespectively(
                first => first.Should().EndWith(": 1 dropped."),
                second => second.Should().EndWith(": 2 dropped.")
            );
    }

    [Fact]
    public async Task Record_DropsInsideAnUpdate_ShouldReportThemOutsideIt()
    {
        // Arrange
        var time = new ContextCapturingTimeProvider();
        await using var host = await HistoryHost.StartAsync(
            x => x.QueueCapacity = 1,
            services: x => x.AddSingleton<TimeProvider>(time)
        );
        await BusyStoreAsync(host);
        RecordEntries(host, 2);
        var logger = host.Host.Services.GetRequiredService<ILogger<TelegramHistoryWriterTests>>();
        using (logger.BeginScope("update 7"))
        {
            RecordEntries(host, 3, 4);
        }

        // Act
        time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        host.Logs.Entries.Where(x => x.Level == LogLevel.Warning)
            .Should()
            .SatisfyRespectively(
                atOnce => atOnce.Should().Be((LogLevel.Warning, AFull(1), "update 7")),
                aMinuteLater => aMinuteLater.Should().Be((LogLevel.Warning, AFull(1), (string?)null))
            );
    }

    [Fact]
    public async Task Stop_WithDropsNotYetReported_ShouldReportThem()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.QueueCapacity = 1);
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3, 4, 5);
        host.Store.Open();

        // Act
        await host.Host.StopAsync();

        // Assert
        host.Logs.At(LogLevel.Warning)
            .Should()
            .SatisfyRespectively(
                first => first.Should().EndWith(": 1 dropped."),
                second => second.Should().EndWith(": 2 dropped.")
            );
    }

    [Fact]
    public async Task Record_AfterStop_ShouldReturnFalseWithoutLogging()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        await host.Host.StopAsync();

        // Act
        var recorded = host.Writer.Record(HistoryHost.Entry(1));

        // Assert
        using (new AssertionScope())
        {
            recorded.Should().BeFalse();
            host.Logs.Entries.Should().NotContain(x => x.Level >= LogLevel.Warning);
        }
    }

    [Fact]
    public async Task Record_AfterTheHostIsDisposedWithoutStopping_ShouldNotLogAnError()
    {
        // Arrange
        var host = await HistoryHost.StartAsync();
        var writer = host.Writer;
        host.Host.Dispose();

        // Act
        var recorded = writer.Record(HistoryHost.Entry(1));

        // Assert
        using (new AssertionScope())
        {
            recorded.Should().BeFalse();
            writer.FlushAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();
            host.Logs.At(LogLevel.Error).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Store_ThatThrows_ShouldLogAnErrorAndStoreLaterEntries()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        host.Store.Failure = new InvalidOperationException("The database is down.");
        RecordEntries(host, 1);
        await host.Writer.FlushAsync(CancellationToken.None);
        host.Store.Failure = null;

        // Act
        RecordEntries(host, 2);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            Texts(host.Store.Entries).Should().Equal("#2");
            host.Logs.At(LogLevel.Error).Should().Equal("The Telegram history store failed; a batch of 1 was dropped.");
        }
    }

    [Fact]
    public async Task Store_ThatIsScoped_ShouldBeResolvedInAScopeOfItsOwnPerBatch()
    {
        // Arrange
        var stores = new List<ScopedStore>();
        await using var host = await HistoryHost.StartAsync(services: x =>
            x.AddScoped<ITelegramHistoryStore>(_ =>
            {
                var store = new ScopedStore();
                stores.Add(store);
                return store;
            })
        );

        // Act
        RecordEntries(host, 1);
        await host.Writer.FlushAsync(CancellationToken.None);
        RecordEntries(host, 2);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            stores.Should().HaveCount(2).And.OnlyContain(x => x.Disposed);
            stores.Select(x => x.Appended).Should().Equal(1, 1);
        }
    }

    [Fact]
    public async Task Filter_ShouldStoreTheEntryItReturns()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.Filter = entry => entry with { Text = null });

        // Act
        RecordEntries(host, 1);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        host.Store.Entries.Should().ContainSingle().Which.Text.Should().BeNull();
    }

    [Fact]
    public async Task Filter_ReturningNull_ShouldDropTheEntry()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x => x.Filter = entry => entry.Text == "#1" ? null : entry);

        // Act
        RecordEntries(host, 1, 2);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        Texts(host.Store.Entries).Should().Equal("#2");
    }

    [Fact]
    public async Task Filter_ThatThrows_ShouldDropTheEntryAndLogAnError()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(x =>
            x.Filter = entry => entry.Text == "#1" ? throw new InvalidOperationException("Broken filter.") : entry
        );

        // Act
        RecordEntries(host, 1, 2);
        await host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            Texts(host.Store.Entries).Should().Equal("#2");
            host.Logs.At(LogLevel.Error)
                .Should()
                .Equal("The Telegram history filter failed on a message entry; it was dropped.");
        }
    }

    [Fact]
    public async Task FlushAsync_ShouldWaitForEntriesRecordedBeforeIt()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        host.Store.Close();
        RecordEntries(host, 1);
        var flush = host.Writer.FlushAsync(CancellationToken.None);
        await host.Store.Entered;
        var whileStoring = flush.IsCompleted;

        // Act
        host.Store.Open();
        await flush;

        // Assert
        using (new AssertionScope())
        {
            whileStoring.Should().BeFalse();
            Texts(host.Store.Entries).Should().Equal("#1");
        }
    }

    [Fact]
    public async Task FlushAsync_BeforeStartOrAfterStop_ShouldReturnAtOnce()
    {
        // Arrange
        await using var host = HistoryHost.Build();
        RecordEntries(host, 1);

        // Act
        var beforeStart = host.Writer.FlushAsync(CancellationToken.None);
        await host.Host.StartAsync();
        await host.Host.StopAsync();
        var afterStop = host.Writer.FlushAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            beforeStart.IsCompletedSuccessfully.Should().BeTrue();
            afterStop.IsCompletedSuccessfully.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Stop_ShouldStoreWhatIsStillQueued()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3);

        // Act
        var stopping = host.Host.StopAsync();
        host.Store.Open();
        await stopping;

        // Assert
        Texts(host.Store.Entries).Should().Equal("#1", "#2", "#3");
    }

    [Fact]
    public async Task Stop_WhenTheStoreHangs_ShouldGiveUpAtTheShutdownTimeoutAndWarnWithTheCount()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync(services: x =>
            x.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMilliseconds(100))
        );
        await BusyStoreAsync(host);
        RecordEntries(host, 2, 3);

        // Act
        await host.Host.StopAsync();

        // Assert
        using (new AssertionScope())
        {
            host.Store.Entries.Should().BeEmpty();
            host.Logs.At(LogLevel.Warning)
                .Should()
                .Equal("The Telegram history wasn't fully stored at shutdown: 3 left.");
            host.Logs.At(LogLevel.Error).Should().BeEmpty();
        }
    }

    // The warning for `dropped` entries, with a queue of 1.
    private static string AFull(int dropped) => $"The Telegram history queue is full (1 entries): {dropped} dropped.";

    // Records #1 and waits until the store is busy storing it.
    private static async Task BusyStoreAsync(HistoryHost host)
    {
        host.Store.Close();
        RecordEntries(host, 1);
        await host.Store.Entered;
    }

    private static void RecordEntries(HistoryHost host, params int[] numbers)
    {
        foreach (var n in numbers)
        {
            host.Writer.Record(HistoryHost.Entry(n));
        }
    }

    private static IEnumerable<string?> Texts(IEnumerable<TelegramHistoryEntry> entries) => entries.Select(x => x.Text);

    private sealed class ScopedStore : ITelegramHistoryStore, IDisposable
    {
        public int Appended { get; private set; }

        public bool Disposed { get; private set; }

        public Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
        {
            Appended += entries.Count;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
            TelegramHistoryQuery query,
            CancellationToken token
        ) => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }
}
