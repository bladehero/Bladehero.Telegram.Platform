using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
}
