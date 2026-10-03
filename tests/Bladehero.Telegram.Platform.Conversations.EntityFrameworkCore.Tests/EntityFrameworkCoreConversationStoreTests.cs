using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkCoreConversationStoreTests
{
    private const long Group = -1001234567890;
    private static readonly ConversationKey Nick = new(Group, 7000000001);
    private static readonly ConversationKey Anna = new(Group, 7000000002);
    private static readonly ConversationState Size = new("coffee", "size", """{"CardId":10}""") { Id = "k3j9x2ab" };

    [Fact]
    public async Task GetAsync_WhenThereIsNone_ShouldReturnNull()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);

        // Act
        var state = await sut.GetAsync(Nick, CancellationToken.None);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ShouldReturnTheStateWithItsRunId()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);

        // Act
        await sut.SaveAsync(Nick, Size, CancellationToken.None);
        var state = await sut.GetAsync(Nick, CancellationToken.None);

        // Assert
        state.Should().Be(Size);
    }

    [Fact]
    public async Task SaveAsync_Again_ShouldReplaceIt()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);
        await sut.SaveAsync(Nick, Size, CancellationToken.None);
        var name = new ConversationState("signup", "name");

        // Act
        await sut.SaveAsync(Nick, name, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            (await sut.GetAsync(Nick, CancellationToken.None)).Should().Be(name);
            (await RowsAsync(database)).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task SaveAsync_AgainWhenTheAppsContextDoesNotTrack_ShouldReplaceIt()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(
            database,
            configure: x => x.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
        );
        var sut = StoreOf(provider);
        await sut.SaveAsync(Nick, Size, CancellationToken.None);

        // Act
        await sut.SaveAsync(Nick, Size with { Step = "name" }, CancellationToken.None);

        // Assert
        (await sut.GetAsync(Nick, CancellationToken.None))
            .Should()
            .Be(Size with { Step = "name" });
    }

    [Fact]
    public async Task SaveAsync_ShouldStampUpdatedAtFromTheAppsClock()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var provider = Provider(database, clock);
        var sut = StoreOf(provider);
        await sut.SaveAsync(Nick, Size, CancellationToken.None);
        var saved = (await RowsAsync(database)).Single().UpdatedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        await sut.SaveAsync(Nick, Size with { Step = "name" }, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            saved.Should().Be(clock.Start);
            (await RowsAsync(database)).Single().UpdatedAt.Should().Be(clock.GetUtcNow());
        }
    }

    [Fact]
    public async Task SaveAsync_ShouldWriteApartFromTheAppsChanges()
    {
        // Arrange: a step's scope, with a change the app hasn't saved yet.
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        await using var scope = provider.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<BudgetContext>();
        app.Expenses.Add(new Expense { Name = "Milk" });
        var sut = scope.ServiceProvider.GetRequiredService<IConversationStore>();

        // Act
        await sut.SaveAsync(Nick, Size, CancellationToken.None);
        var expensesBeforeTheAppSaves = await CountAsync<Expense>(database);
        await app.SaveChangesAsync();

        // Assert
        using (new AssertionScope())
        {
            expensesBeforeTheAppSaves.Should().Be(0);
            app.ChangeTracker.Entries<StoredConversation>().Should().BeEmpty();
            (await CountAsync<Expense>(database)).Should().Be(1);
            (await CountAsync<StoredConversation>(database)).Should().Be(1);
        }
    }

    [Fact]
    public async Task SaveAsync_ForManyChatsAtOnce_ShouldKeepEach()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);
        var keys = Enumerable.Range(0, 20).Select(x => new ConversationKey(Group - x, 7000000001 + x)).ToList();

        // Act
        await Task.WhenAll(
            keys.Select(x => sut.SaveAsync(x, Size with { Id = $"run{x.UserId}" }, CancellationToken.None))
        );

        // Assert
        (await RowsAsync(database))
            .Select(x => (x.ChatId, x.UserId, x.RunId))
            .Should()
            .BeEquivalentTo(keys.Select(x => (x.ChatId, x.UserId, $"run{x.UserId}")));
    }

    [Fact]
    public async Task SaveAsync_ForOneKeyAtOnce_ShouldKeepOneRow()
    {
        // Arrange: both saves find no row, then both add it.
        await using var database = await TestDatabase.CreateAsync();
        var saves = new SimultaneousSaves();
        await using var provider = Provider(database, configure: x => x.AddInterceptors(saves));
        var sut = StoreOf(provider);

        // Act
        await Task.WhenAll(
            sut.SaveAsync(Nick, Size, CancellationToken.None),
            sut.SaveAsync(Nick, Size with { Step = "name" }, CancellationToken.None)
        );

        // Assert: the save that lost the race updated the row in a retry, so it wrote last.
        var rows = await RowsAsync(database);
        using (new AssertionScope())
        {
            saves.Steps.Should().HaveCount(3);
            rows.Should().ContainSingle().Which.Step.Should().Be(saves.Steps.Last());
        }
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveIt()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);
        await sut.SaveAsync(Nick, Size, CancellationToken.None);

        // Act
        await sut.RemoveAsync(Nick, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            (await sut.GetAsync(Nick, CancellationToken.None)).Should().BeNull();
            (await RowsAsync(database)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RemoveAsync_WhenThereIsNone_ShouldDoNothing()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Provider(database);
        var sut = StoreOf(provider);
        await sut.SaveAsync(Anna, Size, CancellationToken.None);

        // Act
        await sut.RemoveAsync(Nick, CancellationToken.None);

        // Assert
        (await sut.GetAsync(Anna, CancellationToken.None))
            .Should()
            .Be(Size);
    }

    private static ServiceProvider Provider(
        TestDatabase database,
        TimeProvider? clock = null,
        Action<DbContextOptionsBuilder>? configure = null
    )
    {
        var services = database.AddTo(new ServiceCollection(), configure);
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddTelegramConversations().UseEntityFrameworkCore<BudgetContext>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static IConversationStore StoreOf(IServiceProvider provider) =>
        provider.GetRequiredService<IConversationStore>();

    private static async Task<List<StoredConversation>> RowsAsync(TestDatabase database)
    {
        await using var context = database.NewContext();
        return await context.Set<StoredConversation>().AsNoTracking().ToListAsync();
    }

    private static async Task<int> CountAsync<T>(TestDatabase database)
        where T : class
    {
        await using var context = database.NewContext();
        return await context.Set<T>().CountAsync();
    }

    // Holds the first two saves until both are ready, so both add the row at once; records each save's step.
    private sealed class SimultaneousSaves : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _saves;

        public ConcurrentQueue<string> Steps { get; } = new();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Steps.Enqueue(eventData.Context!.ChangeTracker.Entries<StoredConversation>().Single().Entity.Step);
            if (Interlocked.Increment(ref _saves) == 2)
            {
                _bothReady.SetResult();
            }

            await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
}
