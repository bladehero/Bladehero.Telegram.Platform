using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

// A migrated SQLite file of its own, deleted on dispose. A file, not a shared in-memory connection: the writer and the
// test use contexts at the same time, and one SqliteConnection isn't thread-safe.
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.db");

    private TestDatabase() { }

    public string ConnectionString => $"Data Source={_path};Pooling=False";

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = Unmigrated();
        await using var context = database.NewContext();
        await context.Database.MigrateAsync();
        return database;
    }

    // For the app to migrate as it starts.
    public static TestDatabase Unmigrated() => new();

    public BudgetContext NewContext() =>
        new(new DbContextOptionsBuilder<BudgetContext>().UseSqlite(ConnectionString).Options);

    // The app's registration of its context, on this database.
    public IServiceCollection AddTo(IServiceCollection services) =>
        services.AddDbContext<BudgetContext>(options => options.UseSqlite(ConnectionString));

    public ValueTask DisposeAsync()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }

        return ValueTask.CompletedTask;
    }
}
