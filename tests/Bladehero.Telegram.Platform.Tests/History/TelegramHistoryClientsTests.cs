using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class TelegramHistoryClientsTests
{
    [Fact]
    public void Decorate_OfANonDisposableClient_ShouldNotBeDisposable()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        services.AddTransient(_ => Mock.Of<ITelegramBotClient>());

        // Act
        services.AddTelegramHistory().Services.AddSingleton(Mock.Of<ITelegramHistoryStore>());

        // Assert: the container keeps no transient wrapper alive to dispose it.
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITelegramBotClient>();
        using (new AssertionScope())
        {
            client.Should().BeOfType<RecordingBotClient>();
            client.Should().NotBeAssignableTo<IDisposable>().And.NotBeAssignableTo<IAsyncDisposable>();
        }
    }
}
