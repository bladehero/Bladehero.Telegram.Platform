using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkCoreConversationsDependencyInjectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UseEntityFrameworkCore_ShouldReplaceTheInMemoryStore(bool beforeTheReceivingSetup)
    {
        // Arrange
        var services = new ServiceCollection().AddDbContext<BudgetContext>(x => x.UseSqlite("Data Source=unused.db"));

        // Act
        if (beforeTheReceivingSetup)
        {
            services.AddTelegramConversations().UseEntityFrameworkCore<BudgetContext>();
        }

        services.AddTelegramReceiving(typeof(BudgetContext).Assembly);
        if (!beforeTheReceivingSetup)
        {
            services.AddTelegramConversations().UseEntityFrameworkCore<BudgetContext>();
        }

        // Assert
        using var provider = services.BuildServiceProvider();
        provider
            .GetServices<IConversationStore>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<EntityFrameworkCoreConversationStore<BudgetContext>>();
    }

    [Fact]
    public async Task Start_WithoutTheMapping_ShouldFailNamingMapTelegramConversations()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddDbContext<UnmappedContext>(options => options.UseSqlite("Data Source=unused.db"));
        builder.Services.AddTelegramConversations().UseEntityFrameworkCore<UnmappedContext>();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage(
                "UnmappedContext doesn't map the Telegram conversations: call modelBuilder.MapTelegramConversations()*"
            );
    }

    [Fact]
    public async Task Start_WithoutTheContext_ShouldFailNamingAddDbContext()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramConversations().UseEntityFrameworkCore<BudgetContext>();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("BudgetContext isn't registered; add it with AddDbContext<BudgetContext>().");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_AfterPickingAnotherContext_ShouldCheckOnlyThatOne(bool registerTheFirst)
    {
        // Arrange: the first context fails a check, unmapped or unregistered.
        var builder = Host.CreateEmptyApplicationBuilder(new());
        if (registerTheFirst)
        {
            builder.Services.AddDbContext<UnmappedContext>(options => options.UseSqlite("Data Source=unused.db"));
        }

        builder.Services.AddDbContext<BudgetContext>(options => options.UseSqlite("Data Source=unused.db"));
        builder
            .Services.AddTelegramConversations()
            .UseEntityFrameworkCore<UnmappedContext>()
            .UseEntityFrameworkCore<BudgetContext>();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    // A context that forgot MapTelegramConversations.
    private sealed class UnmappedContext(DbContextOptions<UnmappedContext> options) : DbContext(options)
    {
        public DbSet<Expense> Expenses => Set<Expense>();
    }
}
