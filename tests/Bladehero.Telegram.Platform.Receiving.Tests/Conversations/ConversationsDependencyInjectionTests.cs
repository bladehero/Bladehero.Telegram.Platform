using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Conversations;

public sealed class ConversationsDependencyInjectionTests
{
    [Fact]
    public void AddTelegramConversations_Twice_ShouldRegisterOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramConversations();

        // Act
        services.AddTelegramConversations();

        // Assert
        services
            .Select(x => x.ServiceType)
            .Should()
            .BeEquivalentTo([
                typeof(IConversationStore),
                typeof(ConversationLocks),
                typeof(Conversation),
                typeof(IConversation),
            ]);
    }

    [Fact]
    public void AddTelegramConversations_ShouldReturnABuilderOverTheSameServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var conversations = services.AddTelegramConversations();

        // Assert
        conversations.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddTelegramConversations_WithAStoreRegisteredFirst_ShouldKeepIt()
    {
        // Arrange
        var store = Mock.Of<IConversationStore>();
        var services = new ServiceCollection().AddSingleton(store);

        // Act
        services.AddTelegramConversations();

        // Assert
        services.BuildServiceProvider().GetRequiredService<IConversationStore>().Should().BeSameAs(store);
    }
}
