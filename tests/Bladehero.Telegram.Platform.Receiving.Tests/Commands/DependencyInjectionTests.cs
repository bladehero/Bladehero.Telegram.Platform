using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
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
    public void AddTelegramCommands_WhenCommandDerivesFromKnownUserCommand_ShouldInjectTheMatchingUserResolver()
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

    private sealed record DiTestUser(string Name);

    private sealed class DiTestResolver : ITelegramUserResolver<DiTestUser>
    {
        public Task<DiTestUser?> ResolveAsync(long chatId, CancellationToken token) =>
            Task.FromResult<DiTestUser?>(null);
    }

    private sealed class DiProbeCommand : KnownUserCommand<DiTestUser>
    {
        public bool HasResolver => UserResolver is not null;

        protected override bool Matches(Message message) => false;

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
