using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkCoreTelegramHistoryStoreTests
{
    private const long Group = -1001234567890;
    private const long OtherGroup = -1009876543210;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_ShouldStoreThroughAContextOfItsOwn_LeavingTheAppsChangesUnsaved()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        var services = database.AddTo(new ServiceCollection().AddLogging());
        services.AddTelegramHistory().UseEntityFrameworkCore<BudgetContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var appScope = provider.CreateAsyncScope();
        var app = appScope.ServiceProvider.GetRequiredService<BudgetContext>();
        app.Expenses.Add(new Expense { Name = "Milk" });

        // Act
        await using (var writerScope = provider.CreateAsyncScope())
        {
            var store = writerScope.ServiceProvider.GetRequiredService<ITelegramHistoryStore>();
            await store.AppendAsync([Entry("#1")], CancellationToken.None);
        }

        var expensesBeforeTheAppSaves = await CountAsync<Expense>(database);
        await app.SaveChangesAsync();

        // Assert
        using (new AssertionScope())
        {
            expensesBeforeTheAppSaves.Should().Be(0);
            app.ChangeTracker.Entries<TelegramHistoryEntry>().Should().BeEmpty();
            (await CountAsync<Expense>(database)).Should().Be(1);
            (await CountAsync<TelegramHistoryEntry>(database)).Should().Be(1);
        }
    }

    [Fact]
    public async Task AppendAsync_ShouldGiveIncreasingIds()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();

        // Act
        await AppendAsync(database, Entry("#1"), Entry("#2"));
        await AppendAsync(database, Entry("#3", chatId: OtherGroup));

        // Assert
        (await ReadAsync(database, new()))
            .Select(x => (x.Id, x.Text))
            .Should()
            .Equal((1, "#1"), (2, "#2"), (3, "#3"));
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTheLatestOldestFirst()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(database, Entry("#1"), Entry("#2"), Entry("other", chatId: OtherGroup), Entry("#3"));

        // Act
        var entries = await ReadAsync(database, new() { ChatId = Group, Limit = 2 });

        // Assert
        Texts(entries).Should().Equal("#2", "#3");
    }

    [Fact]
    public async Task ReadAsync_BeforeAnId_ShouldReturnThePageBeforeIt()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(database, Entry("#1"), Entry("#2"), Entry("#3"), Entry("#4"), Entry("#5"));
        var query = new TelegramHistoryQuery { ChatId = Group, Limit = 2 };
        var latest = await ReadAsync(database, query);

        // Act
        var before = await ReadAsync(database, query with { BeforeId = latest[0].Id });

        // Assert
        Texts(before).Should().Equal("#2", "#3");
    }

    [Fact]
    public async Task ReadAsync_SinceATimeWithAnOffset_ShouldCompareTheInstant()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(
            database,
            Entry("#1", time: Now.AddMinutes(-2)),
            Entry("#2", time: Now.AddMinutes(-1)),
            Entry("#3", time: Now)
        );

        // Act: the same instant as #2, at +03:00.
        var entries = await ReadAsync(database, new() { Since = Now.AddMinutes(-1).ToOffset(TimeSpan.FromHours(3)) });

        // Assert
        Texts(entries).Should().Equal("#2", "#3");
    }

    [Fact]
    public async Task ReadAsync_ByMessage_ShouldReturnItsLifecycle()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(
            database,
            Entry("Latte?", kind: "sendMessage", messageId: 10),
            Entry("Large latte?", kind: "editMessageText", messageId: 10),
            Entry("Hi", kind: "sendMessage", messageId: 11),
            Entry("size:large", kind: "callback_query", messageId: 10),
            Entry("Elsewhere", kind: "sendMessage", chatId: OtherGroup, messageId: 10),
            Entry(null, kind: "deleteMessage", messageId: 10)
        );

        // Act
        var entries = await ReadAsync(database, new() { ChatId = Group, MessageId = 10 });

        // Assert
        entries
            .Select(x => x.Kind)
            .Should()
            .Equal("sendMessage", "editMessageText", "callback_query", "deleteMessage");
    }

    [Fact]
    public async Task ReadAsync_ByUpdate_ShouldReturnItAndWhatItCaused()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await AppendAsync(
            database,
            Entry("size:large", kind: "callback_query", updateId: 7),
            Entry("/start", kind: "message", updateId: 8),
            Entry("Saved", kind: "answerCallbackQuery", updateId: 7),
            Entry("New order", kind: "sendMessage", chatId: OtherGroup, updateId: 7),
            Entry("Done", kind: "editMessageText", chatId: null, updateId: 7)
        );

        // Act
        var entries = await ReadAsync(database, new() { UpdateId = 7 });

        // Assert
        entries
            .Select(x => x.Kind)
            .Should()
            .Equal("callback_query", "answerCallbackQuery", "sendMessage", "editMessageText");
    }

    [Fact]
    public async Task Time_ShouldComeBackAsTheSameInstant()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        var time = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.FromHours(3));
        await AppendAsync(database, Entry("#1", time: time));

        // Act
        var entry = (await ReadAsync(database, new())).Single();

        // Assert
        entry.Time.Should().Be(time);
    }

    internal static TelegramHistoryEntry Entry(
        string? text,
        long? chatId = Group,
        string kind = "message",
        int? messageId = null,
        int? updateId = null,
        DateTimeOffset? time = null
    ) =>
        new()
        {
            Kind = kind,
            Text = text,
            ChatId = chatId,
            MessageId = messageId,
            UpdateId = updateId,
            Time = time ?? Now,
        };

    internal static async Task AppendAsync(TestDatabase database, params TelegramHistoryEntry[] entries)
    {
        await using var context = database.NewContext();
        await new EntityFrameworkCoreTelegramHistoryStore<BudgetContext>(context).AppendAsync(
            entries,
            CancellationToken.None
        );
    }

    internal static async Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
        TestDatabase database,
        TelegramHistoryQuery query
    )
    {
        await using var context = database.NewContext();
        return await new EntityFrameworkCoreTelegramHistoryStore<BudgetContext>(context).ReadAsync(
            query,
            CancellationToken.None
        );
    }

    private static async Task<int> CountAsync<T>(TestDatabase database)
        where T : class
    {
        await using var context = database.NewContext();
        return await context.Set<T>().CountAsync();
    }

    private static IEnumerable<string?> Texts(IEnumerable<TelegramHistoryEntry> entries) => entries.Select(x => x.Text);
}
