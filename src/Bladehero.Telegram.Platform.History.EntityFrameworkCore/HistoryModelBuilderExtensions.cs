using Microsoft.EntityFrameworkCore;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore;

/// <summary>Maps the bot's history into an app's <see cref="DbContext"/>.</summary>
public static class HistoryModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="TelegramHistoryEntry"/> to <paramref name="tableName"/>; call it in <c>OnModelCreating</c>, then
    /// add a migration.
    /// </summary>
    /// <param name="modelBuilder">The app's model builder.</param>
    /// <param name="tableName">The table the history goes to.</param>
    /// <param name="schema">The table's schema, or the provider's default when <c>null</c>.</param>
    /// <returns><paramref name="modelBuilder"/>, for chaining.</returns>
    public static ModelBuilder MapTelegramHistory(
        this ModelBuilder modelBuilder,
        string tableName = "TelegramHistory",
        string? schema = null
    )
    {
        modelBuilder.Entity<TelegramHistoryEntry>(entry =>
        {
            entry.ToTable(tableName, schema);
            entry.HasKey(x => x.Id);

            // A UTC date and time, as every provider can compare and order one; SQLite can't a DateTimeOffset.
            entry
                .Property(x => x.Time)
                .HasConversion(
                    time => time.UtcDateTime,
                    stored => new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc))
                );

            // Only what the library controls is bounded, so a long text or file name can't fail a batch.
            entry.Property(x => x.Direction).HasConversion<string>().HasMaxLength(8);
            entry.Property(x => x.Kind).HasMaxLength(64);
            entry.Property(x => x.InlineMessageId).HasMaxLength(128);

            entry.HasIndex(x => new { x.ChatId, x.Id });
            entry.HasIndex(x => new { x.ChatId, x.MessageId });
            entry.HasIndex(x => x.InlineMessageId);
            entry.HasIndex(x => x.UpdateId);
            entry.HasIndex(x => x.Time);
        });

        return modelBuilder;
    }
}
