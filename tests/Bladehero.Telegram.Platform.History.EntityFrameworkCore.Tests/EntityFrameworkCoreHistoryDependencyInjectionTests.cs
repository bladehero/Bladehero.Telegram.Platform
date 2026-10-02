using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.History.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkCoreHistoryDependencyInjectionTests
{
    [Fact]
    public void UseEntityFrameworkCore_WithAMaxAgeThatIsNotPositive_ShouldThrow()
    {
        // Arrange
        var history = new ServiceCollection().AddTelegramHistory();

        // Act
        var act = () => history.UseEntityFrameworkCore<BudgetContext>(maxAge: TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxAge");
    }

    [Fact]
    public async Task Start_WithoutTheMapping_ShouldFailNamingMapTelegramHistory()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddDbContext<UnmappedContext>(options => options.UseSqlite("Data Source=unused.db"));
        builder.Services.AddTelegramHistory().UseEntityFrameworkCore<UnmappedContext>();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("UnmappedContext doesn't map the Telegram history: call modelBuilder.MapTelegramHistory()*");
    }

    [Fact]
    public async Task Start_WithoutTheContext_ShouldFailNamingAddDbContext()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory().UseEntityFrameworkCore<BudgetContext>();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("BudgetContext isn't registered; add it with AddDbContext<BudgetContext>().");
    }

    [Fact]
    public async Task History_OfAComponentTest_ShouldBeInTheAppsDatabase()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        await using var bot = await TelegramTestHost.ForLongPollingAsync(
            (IServiceCollection services) =>
            {
                services.AddTelegramLongPollingReceiving(
                    receiver => receiver.Token = "unused",
                    typeof(EchoCommand).Assembly
                );
                database.AddTo(services);
                services.AddTelegramHistory().UseEntityFrameworkCore<BudgetContext>();
            }
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hi");

        // Assert: the action has already waited for the history.
        await using var scope = bot.Services.CreateAsyncScope();
        var kinds = await scope
            .ServiceProvider.GetRequiredService<BudgetContext>()
            .Set<TelegramHistoryEntry>()
            .Where(x => x.ChatId == nick.Chat.Id)
            .OrderBy(x => x.Id)
            .Select(x => x.Kind)
            .ToListAsync();
        kinds.Should().Equal("message", "sendMessage");
    }

    // A context that forgot MapTelegramHistory.
    private sealed class UnmappedContext(DbContextOptions<UnmappedContext> options) : DbContext(options)
    {
        public DbSet<Expense> Expenses => Set<Expense>();
    }

    // The bot of the component test: it echoes text.
    private sealed class EchoCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.Text is not null);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, request.Payload.Text!, cancellationToken: token);
    }
}
