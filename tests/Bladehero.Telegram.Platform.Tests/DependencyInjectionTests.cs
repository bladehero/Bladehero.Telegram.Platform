using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddTelegramBot_WithoutHistory_ShouldRegisterThePlainClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTelegramBot(x => x.Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw");

        // Assert
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITelegramBotClient>().Should().BeOfType<TelegramBotClient>();
    }
}
