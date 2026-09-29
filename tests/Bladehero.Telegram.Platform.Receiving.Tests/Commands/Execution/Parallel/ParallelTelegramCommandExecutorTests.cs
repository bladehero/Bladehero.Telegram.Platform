using System.Diagnostics;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Execution.Parallel;

public sealed class ParallelTelegramCommandExecutorTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Precision = TimeSpan.FromMilliseconds(400);

    private static readonly ITelegramBotClient Client = Mock.Of<ITelegramBotClient>();

    private static readonly CommandRequest Request = new(
        new Update
        {
            Id = 1,
            Message = new Message
            {
                Text = "/probe",
                Chat = new Chat { Id = 1 },
            },
        },
        Client
    );

    [Fact]
    public async Task ExecuteAsync_WhenGroupsHaveDifferentGlobalPriority_ShouldRunGroupsSequentially()
    {
        // Arrange
        var log = new EventLog();

        var groupAPriority = new CommandPriority(0, 0);
        var groupBPriority = new CommandPriority(1, 0);

        var a1 = log.Command("A1");
        var a2 = log.Command("A2");
        var b1 = log.Command("B1");
        var b2 = log.Command("B2");

        var sut = BuildExecutor(
            5,
            (groupAPriority, a1),
            (groupAPriority, a2),
            (groupBPriority, b1),
            (groupBPriority, b2)
        );

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            log.Events.Should().HaveCount(8);

            var (groupAStart, groupAEnd) = WindowOf(log, "A1", "A2");
            var (groupBStart, groupBEnd) = WindowOf(log, "B1", "B2");

            // Group A runs its two commands concurrently (single round), not one after another.
            AssertRanConcurrently(groupAStart, groupAEnd);

            // Group B only starts once Group A has fully finished, with no wasted idle time in between.
            AssertRunsRightAfter(groupBStart, groupAEnd);

            // Group B also runs its two commands concurrently.
            AssertRanConcurrently(groupBStart, groupBEnd);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSubGroupsShareGlobalPriority_ShouldRunSubGroupsSequentially()
    {
        // Arrange
        var log = new EventLog();

        var firstGroupPriority = new CommandPriority(0, 0);
        var secondGroupPriority = new CommandPriority(0, 1);

        var c1 = log.Command("C1");
        var c2 = log.Command("C2");

        var sut = BuildExecutor(5, (firstGroupPriority, c1), (secondGroupPriority, c2));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            log.Events.Should().Equal("C1:start", "C1:end", "C2:start", "C2:end");

            // The second sub-group (Group=1) only starts once the first (Group=0) has finished.
            AssertRunsRightAfter(log.OffsetOf("C2:start"), log.OffsetOf("C1:end"));

            // Two fully sequential rounds should take roughly 2x a single command's delay.
            (log.OffsetOf("C2:end") - log.OffsetOf("C1:start"))
                .Should()
                .BeCloseTo(Delay * 2, Precision);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandCountIsWithinParallelCount_ShouldRunThemConcurrently()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var e1 = log.Command("E1");
        var e2 = log.Command("E2");

        var sut = BuildExecutor(5, (groupPriority, e1), (groupPriority, e2));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var (groupStart, groupEnd) = WindowOf(log, "E1", "E2");

            // Both commands fit under the ParallelCount cap, so they should run in a single concurrent round.
            AssertRanConcurrently(groupStart, groupEnd);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandCountExceedsParallelCount_ShouldChunkAndRunChunksSequentially()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var f1 = log.Command("F1");
        var f2 = log.Command("F2");
        var f3 = log.Command("F3");

        var sut = BuildExecutor(2, (groupPriority, f1), (groupPriority, f2), (groupPriority, f3));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var (firstChunkStart, firstChunkEnd) = WindowOf(log, "F1", "F2");
            var secondChunkStart = log.OffsetOf("F3:start");
            var secondChunkEnd = log.OffsetOf("F3:end");

            // The first chunk (F1, F2) fills the cap of 2, so it runs concurrently as one round.
            AssertRanConcurrently(firstChunkStart, firstChunkEnd);

            // The third command overflows the cap and only starts once the first chunk has fully finished.
            AssertRunsRightAfter(secondChunkStart, firstChunkEnd);

            // Two sequential rounds should take roughly 2x a single command's delay.
            (secondChunkEnd - firstChunkStart)
                .Should()
                .BeCloseTo(Delay * 2, Precision);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenParallelCountIsNotConfigured_ShouldRunAllCommandsInAGroupConcurrently()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var g1 = log.Command("G1");
        var g2 = log.Command("G2");
        var g3 = log.Command("G3");

        var sut = BuildExecutor(null, (groupPriority, g1), (groupPriority, g2), (groupPriority, g3));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var (groupStart, groupEnd) = WindowOf(log, "G1", "G2", "G3");

            // With no ParallelCount configured, the whole group (3 commands) runs unbounded, in one round.
            AssertRanConcurrently(groupStart, groupEnd);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoCommandsAreRegistered_ShouldCompleteWithoutError()
    {
        // Arrange
        var sut = BuildExecutor(5);

        // Act
        var act = () => sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenACommandCannotHandleTheRequest_ShouldSkipItButRunTheRest()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var h1 = log.Command("H1");
        var h2 = log.Command("H2", canHandle: false);

        var sut = BuildExecutor(5, (groupPriority, h1), (groupPriority, h2));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            log.Events.Should().Equal("H1:start", "H1:end");
            h2.HandleCalled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoCommandInAGroupCanHandleTheRequest_ShouldSkipTheGroupAndContinue()
    {
        // Arrange
        var log = new EventLog();

        var groupAPriority = new CommandPriority(0, 0);
        var groupBPriority = new CommandPriority(1, 0);

        var i1 = log.Command("I1", canHandle: false);
        var j1 = log.Command("J1");

        var sut = BuildExecutor(5, (groupAPriority, i1), (groupBPriority, j1));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            i1.HandleCalled.Should().BeFalse();
            log.Events.Should().Equal("J1:start", "J1:end");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenGroupSizeExactlyMatchesParallelCount_ShouldRunAsOneChunk()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var k1 = log.Command("K1");
        var k2 = log.Command("K2");

        var sut = BuildExecutor(2, (groupPriority, k1), (groupPriority, k2));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var (groupStart, groupEnd) = WindowOf(log, "K1", "K2");

            // A group exactly the size of the cap must not spill into a wasted second, empty round.
            AssertRanConcurrently(groupStart, groupEnd);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenParallelCountIsOne_ShouldRunGroupCommandsSequentially()
    {
        // Arrange
        var log = new EventLog();

        var groupPriority = new CommandPriority(0, 0);

        var l1 = log.Command("L1");
        var l2 = log.Command("L2");

        var sut = BuildExecutor(1, (groupPriority, l1), (groupPriority, l2));

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            log.Events.Should().Equal("L1:start", "L1:end", "L2:start", "L2:end");

            // A cap of exactly 1 (bounded) must serialize commands, unlike an unconfigured (unbounded) cap.
            AssertRunsRightAfter(log.OffsetOf("L2:start"), log.OffsetOf("L1:end"));
            (log.OffsetOf("L2:end") - log.OffsetOf("L1:start")).Should().BeCloseTo(Delay * 2, Precision);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenMultipleGroupsHaveDifferentSizes_ShouldChunkEachGroupIndependently()
    {
        // Arrange
        var log = new EventLog();

        var groupAPriority = new CommandPriority(0, 0);
        var groupBPriority = new CommandPriority(1, 0);

        var m1 = log.Command("M1");

        var n1 = log.Command("N1");
        var n2 = log.Command("N2");
        var n3 = log.Command("N3");
        var n4 = log.Command("N4");

        var sut = BuildExecutor(
            3,
            (groupAPriority, m1),
            (groupBPriority, n1),
            (groupBPriority, n2),
            (groupBPriority, n3),
            (groupBPriority, n4)
        );

        // Act
        await sut.ExecuteAsync(Request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var (groupBFirstChunkStart, groupBFirstChunkEnd) = WindowOf(log, "N1", "N2", "N3");

            // Group A (a single command, below the cap) is not artificially chunked.
            AssertRunsRightAfter(groupBFirstChunkStart, log.OffsetOf("M1:end"));

            // Group B's first chunk (3 commands, exactly the cap) runs concurrently.
            AssertRanConcurrently(groupBFirstChunkStart, groupBFirstChunkEnd);

            // Group B's 4th command overflows the cap and only starts once the first chunk finishes.
            AssertRunsRightAfter(log.OffsetOf("N4:start"), groupBFirstChunkEnd);

            // Group B takes two sequential rounds in total.
            (log.OffsetOf("N4:end") - groupBFirstChunkStart)
                .Should()
                .BeCloseTo(Delay * 2, Precision);
        }
    }

    private static ParallelTelegramCommandExecutor BuildExecutor(
        int? parallelCount,
        params (CommandPriority Priority, ITelegramCommand Command)[] commands
    )
    {
        var commandAccessor = new CommandPriorityAccessor(commands);
        return new ParallelTelegramCommandExecutor(
            commandAccessor,
            StaticOptionsMonitor.From(new ParallelCommandExecutionConfiguration { ParallelCount = parallelCount })
        );
    }

    private static void AssertRanConcurrently(TimeSpan groupStart, TimeSpan groupEnd) =>
        (groupEnd - groupStart).Should().BeCloseTo(Delay, Precision);

    private static void AssertRunsRightAfter(TimeSpan later, TimeSpan earlier)
    {
        var gap = later - earlier;
        gap.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        gap.Should().BeLessThan(Precision);
    }

    private static (TimeSpan Start, TimeSpan End) WindowOf(EventLog log, params string[] names) =>
        (
            names.Select(name => log.OffsetOf($"{name}:start")).Min(),
            names.Select(name => log.OffsetOf($"{name}:end")).Max()
        );

    private sealed class EventLog
    {
        private readonly List<(string Name, TimeSpan Offset)> _events = [];
        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public List<string> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events.Select(x => x.Name)];
                }
            }
        }

        public TimeSpan OffsetOf(string name)
        {
            lock (_gate)
            {
                return _events.First(x => x.Name == name).Offset;
            }
        }

        public RecordingCommand Command(string name, bool canHandle = true) => new(name, this, canHandle);

        public void Add(string name)
        {
            var offset = _stopwatch.Elapsed;
            lock (_gate)
            {
                _events.Add((name, offset));
            }
        }
    }

    private sealed class RecordingCommand(string name, EventLog log, bool canHandle) : ITelegramCommand
    {
        public bool HandleCalled { get; private set; }

        public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) => Task.FromResult(canHandle);

        public async Task HandleAsync(CommandRequest request, CancellationToken token)
        {
            HandleCalled = true;
            log.Add($"{name}:start");
            await Task.Delay(Delay, token);
            log.Add($"{name}:end");
        }
    }

    private static class StaticOptionsMonitor
    {
        public static IOptionsMonitor<T> From<T>(T value) => new StaticOptionsMonitor<T>(value);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
