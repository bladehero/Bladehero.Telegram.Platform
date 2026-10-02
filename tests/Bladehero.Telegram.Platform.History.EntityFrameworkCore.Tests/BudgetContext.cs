using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

public sealed class Expense
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

// An app's own context, with the history mapped into it.
public sealed class BudgetContext(DbContextOptions<BudgetContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.MapTelegramHistory();
}

// For dotnet-ef. After a mapping change, from the repository root:
// dotnet-ef migrations add <Name> --context BudgetContext --project tests/Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests
public sealed class BudgetContextFactory : IDesignTimeDbContextFactory<BudgetContext>
{
    public BudgetContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BudgetContext>().UseSqlite("Data Source=budget.db").Options);
}
