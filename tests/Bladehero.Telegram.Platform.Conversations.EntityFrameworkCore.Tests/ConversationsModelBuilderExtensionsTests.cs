using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore.Tests;

public sealed class ConversationsModelBuilderExtensionsTests
{
    [Fact]
    public async Task MapTelegramConversations_ShouldMatchTheMigration()
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
    public void MapTelegramConversations_WithATableAndSchema_ShouldMapThem()
    {
        // Arrange
        using var context = new BotConversationsContext();

        // Act
        var conversation = ConversationsOf(context);

        // Assert
        using (new AssertionScope())
        {
            conversation.GetTableName().Should().Be("BotConversations");
            conversation.GetSchema().Should().Be("telegram");
        }
    }

    [Fact]
    public void MapTelegramConversations_ShouldKeyByChatAndUser()
    {
        // Arrange
        using var context = new BotConversationsContext();

        // Act
        var conversation = ConversationsOf(context);

        // Assert: every read and write is by the key, so nothing else is indexed.
        using (new AssertionScope())
        {
            conversation.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("ChatId", "UserId");
            conversation.GetIndexes().Should().BeEmpty();
        }
    }

    private static IEnumerable<(string Name, int? MaxLength, bool IsNullable)> Facets(IReadOnlyModel model) =>
        model
            .FindEntityType(typeof(StoredConversation).FullName!)!
            .GetProperties()
            .Select(x => (x.Name, x.GetMaxLength(), x.IsNullable))
            .OrderBy(x => x.Name);

    private static IEntityType ConversationsOf(DbContext context) =>
        context.Model.FindEntityType(typeof(StoredConversation))
        ?? throw new InvalidOperationException("The conversations aren't mapped.");

    // Never opens its database: only its model is read.
    private sealed class BotConversationsContext()
        : DbContext(new DbContextOptionsBuilder<BotConversationsContext>().UseSqlite("Data Source=unused.db").Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.MapTelegramConversations(tableName: "BotConversations", schema: "telegram");
    }
}
