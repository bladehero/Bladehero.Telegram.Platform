using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddTelegramCommands_WhenCommandDerivesFromKnownUserMessageCommand_ShouldInjectTheMatchingUserResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITelegramUserResolver<DiTestUser>>(new DiTestResolver());
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        var provider = services.BuildServiceProvider();

        // Act
        var command = provider.GetRequiredService<DiProbeCommand>();

        // Assert
        command.HasResolver.Should().BeTrue();
    }

    [Fact]
    public void AddTelegramCommands_WhenCommandDerivesFromKnownUserCallbackQueryCommand_ShouldInjectTheMatchingUserResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITelegramUserResolver<DiTestUser>>(new DiTestResolver());
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        var provider = services.BuildServiceProvider();

        // Act
        var command = provider.GetRequiredService<DiButtonCommand>();

        // Assert
        command.HasResolver.Should().BeTrue();
    }

    [Fact]
    public void AddTelegramCommands_WhenAKnownUserMessageCommandsDependencyIsMissing_ShouldFailNamingIt()
    {
        // Arrange: nothing registers the ledger.
        var services = new ServiceCollection();
        services.AddSingleton<ITelegramUserResolver<DiTestUser>>(new DiTestResolver());
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);
        var provider = services.BuildServiceProvider();

        // Act
        var act = () => provider.GetRequiredService<DiLedgerCommand>();

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(IDiLedger)}*{nameof(DiLedgerCommand)}*");
    }

    [Fact]
    public void AddTelegramCommands_ShouldExposeTheCommandMenuDeclaredInTheScannedAssembly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var menu = services.BuildServiceProvider().GetRequiredService<IBotCommandMenu>();

        // Assert
        menu.Commands.Should()
            .ContainSingle(x => x.Command == "di_menu")
            .Which.Description.Should()
            .Be("From the scan");
    }

    [Fact]
    public void AddTelegramCommands_ShouldKeepConversationStepsOutOfTheRegularCommands()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var catalog = services.BuildServiceProvider().GetRequiredService<CommandCatalog>();

        // Assert
        using (new AssertionScope())
        {
            catalog.Steps.Select(x => x.Type).Should().Contain(typeof(DiStepCommand));
            catalog.Regular.Select(x => x.Type).Should().NotContain(typeof(DiStepCommand));
            catalog.Regular.Select(x => x.Type).Should().Contain(typeof(DiProbeCommand));
        }
    }

    [Fact]
    public void AddTelegramCommands_ShouldReadThePriorityOfTheScannedCommands()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var catalog = services.BuildServiceProvider().GetRequiredService<CommandCatalog>();

        // Assert
        var priority = catalog.Regular.Single(x => x.Type == typeof(DiPriorityCommand)).Priority;
        using (new AssertionScope())
        {
            priority.Global.Should().Be(1);
            priority.Group.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddTelegramReceiving_WithACustomConversationStore_ShouldUseItWhicheverOrderItIsRegisteredIn(
        bool registeredFirst
    )
    {
        // Arrange
        var custom = Mock.Of<IConversationStore>();
        var services = new ServiceCollection();

        if (registeredFirst)
        {
            services.AddSingleton(custom);
        }

        services.AddTelegramReceiving(typeof(DependencyInjectionTests).Assembly);

        if (!registeredFirst)
        {
            services.AddSingleton(custom);
        }

        // Act
        var store = services.BuildServiceProvider().GetRequiredService<IConversationStore>();

        // Assert
        store.Should().BeSameAs(custom);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddTelegramReceiving_WithACustomButtonRefusalHandler_ShouldUseItWhicheverOrderItIsRegisteredIn(
        bool registeredFirst
    )
    {
        // Arrange
        var services = new ServiceCollection();

        if (registeredFirst)
        {
            services.AddScoped<IButtonRefusalHandler, DiRefusalHandler>();
        }

        services.AddTelegramReceiving(typeof(DependencyInjectionTests).Assembly);

        if (!registeredFirst)
        {
            services.AddScoped<IButtonRefusalHandler, DiRefusalHandler>();
        }

        using var scope = services.BuildServiceProvider().CreateScope();

        // Act
        var handler = scope.ServiceProvider.GetRequiredService<IButtonRefusalHandler>();

        // Assert
        handler.Should().BeOfType<DiRefusalHandler>();
    }

    [Fact]
    public void AddTelegramCommands_ShouldRegisterTheButtonsOfTheScannedAssembly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramCommands([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var buttons = services.BuildServiceProvider().GetRequiredService<ButtonCatalog>();

        // Assert
        using (new AssertionScope())
        {
            buttons.TryFind("di-pick:1", out var codec).Should().BeTrue();
            codec!.Type.Should().Be<DiPick>();
        }
    }

    private sealed record DiTestUser(string Name);

    [ButtonData("di-pick")]
    private readonly record struct DiPick(int Id);

    private sealed class DiRefusalHandler : IButtonRefusalHandler
    {
        public Task HandleAsync(ButtonRefusal refusal, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class DiTestResolver : ITelegramUserResolver<DiTestUser>
    {
        public Task<DiTestUser?> ResolveAsync(long chatId, long userId, CancellationToken token) =>
            Task.FromResult<DiTestUser?>(null);
    }

    private sealed class DiProbeCommand : KnownUserMessageCommand<DiTestUser>
    {
        public bool HasResolver => UserResolver is not null;

        protected override bool Matches(Message message) => false;

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.CompletedTask;
    }

    // No test in this assembly resolves every scanned command, so the missing ledger breaks only the test that asks.
    private interface IDiLedger;

    private sealed class DiLedgerCommand(IDiLedger ledger) : KnownUserMessageCommand<DiTestUser>
    {
        public IDiLedger Ledger { get; } = ledger;

        protected override bool Matches(Message message) => false;

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.CompletedTask;
    }

    private sealed class DiButtonCommand : KnownUserCallbackQueryCommand<DiTestUser, int>
    {
        public bool HasResolver => UserResolver is not null;

        protected override int? Parse(string data) => null;

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
            Task.CompletedTask;
    }

    [BotCommand("di_menu", "From the scan")]
    private sealed class DiMenuCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(false);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.CompletedTask;
    }

    [CommandPriority(1, 0)]
    private sealed class DiPriorityCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(false);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.CompletedTask;
    }

    [ConversationStep("di")]
    private sealed class DiStepCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(false);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.CompletedTask;
    }
}
