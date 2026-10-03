using Microsoft.EntityFrameworkCore;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore;

/// <summary>Maps the bot's conversations into an app's <see cref="DbContext"/>.</summary>
public static class ConversationsModelBuilderExtensions
{
    /// <summary>
    /// Maps the conversations to <paramref name="tableName"/>; call it in <c>OnModelCreating</c>, then add a
    /// migration.
    /// </summary>
    /// <param name="modelBuilder">The app's model builder.</param>
    /// <param name="tableName">The table the conversations go to.</param>
    /// <param name="schema">The table's schema, or the provider's default when <c>null</c>.</param>
    /// <returns><paramref name="modelBuilder"/>, for chaining.</returns>
    public static ModelBuilder MapTelegramConversations(
        this ModelBuilder modelBuilder,
        string tableName = "TelegramConversations",
        string? schema = null
    )
    {
        modelBuilder.Entity<StoredConversation>(conversation =>
        {
            conversation.ToTable(tableName, schema);
            conversation.HasKey(x => new { x.ChatId, x.UserId });

            // The app names its flows and steps, so they aren't bounded.
            conversation.Property(x => x.Flow).IsRequired();
            conversation.Property(x => x.Step).IsRequired();

            // A UTC date and time, as every provider can compare and order one; SQLite can't a DateTimeOffset.
            conversation
                .Property(x => x.UpdatedAt)
                .HasConversion(
                    time => time.UtcDateTime,
                    stored => new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc))
                );
        });

        return modelBuilder;
    }
}
