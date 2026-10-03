using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore.Tests;

// A migrated SQLite file of its own, deleted on dispose. A file, not a shared in-memory connection: the store and the
// test use contexts at the same time, and one SqliteConnection isn't thread-safe.
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"conversations-{Guid.NewGuid():N}.db");

    private TestDatabase() { }

    public string ConnectionString => $"Data Source={_path}";

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase();
        await using var context = database.NewContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public BudgetContext NewContext() =>
        new(new DbContextOptionsBuilder<BudgetContext>().UseSqlite(ConnectionString).Options);

    // The app's registration of its context, on this database.
    public IServiceCollection AddTo(IServiceCollection services, Action<DbContextOptionsBuilder>? configure = null) =>
        services.AddDbContext<BudgetContext>(options =>
        {
            options.UseSqlite(ConnectionString);
            configure?.Invoke(options);
        });

    // Pooled connections keep the file open until the pools are cleared.
    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }

        return ValueTask.CompletedTask;
    }
}
