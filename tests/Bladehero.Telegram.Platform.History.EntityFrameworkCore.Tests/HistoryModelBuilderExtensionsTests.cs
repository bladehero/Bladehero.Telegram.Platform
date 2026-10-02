using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

public sealed class HistoryModelBuilderExtensionsTests
{
    [Fact]
    public async Task MapTelegramHistory_ShouldMatchTheMigration()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.NewContext();

        // Act
        var pending = context.Database.HasPendingModelChanges();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;

        // Assert: SQLite stores every string as TEXT, so its check misses a changed length that other providers don't.
        using (new AssertionScope())
        {
            pending.Should().BeFalse("a mapping change needs a new migration, and a note under Upgrading");
            Facets(snapshot).Should().Equal(Facets(context.Model));
        }
    }

    [Fact]
    public void MapTelegramHistory_WithATableAndSchema_ShouldMapThem()
    {
        // Arrange
        using var context = new BotHistoryContext();

        // Act
        var entry = HistoryOf(context);

        // Assert
        using (new AssertionScope())
        {
            entry.GetTableName().Should().Be("BotHistory");
            entry.GetSchema().Should().Be("telegram");
        }
    }

    [Fact]
    public void MapTelegramHistory_ShouldIndexWhatTheQueriesFilterOn()
    {
        // Arrange
        using var context = new BotHistoryContext();

        // Act
        var indexes = HistoryOf(context).GetIndexes().Select(x => string.Join(", ", x.Properties.Select(p => p.Name)));

        // Assert
        indexes.Should().BeEquivalentTo("ChatId, Id", "ChatId, MessageId", "InlineMessageId", "UpdateId", "Time");
    }

    private static IEnumerable<(string Name, int? MaxLength, bool IsNullable)> Facets(IReadOnlyModel model) =>
        model
            .FindEntityType(typeof(TelegramHistoryEntry).FullName!)!
            .GetProperties()
            .Select(x => (x.Name, x.GetMaxLength(), x.IsNullable))
            .OrderBy(x => x.Name);

    private static IEntityType HistoryOf(DbContext context) =>
        context.Model.FindEntityType(typeof(TelegramHistoryEntry))
        ?? throw new InvalidOperationException("The history isn't mapped.");

    // Never opens its database: only its model is read.
    private sealed class BotHistoryContext()
        : DbContext(new DbContextOptionsBuilder<BotHistoryContext>().UseSqlite("Data Source=unused.db").Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.MapTelegramHistory(tableName: "BotHistory", schema: "telegram");
    }
}
