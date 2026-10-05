using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

public sealed class TelegramLockDependencyInjectionTests
{
    [Fact]
    public void AddTelegramLock_ShouldResolveTheLockUpdatesHold_AsOneInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTelegramLock();

        // Assert
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        using (new AssertionScope())
        {
            provider.GetRequiredService<ITelegramLock>().Should().BeSameAs(provider.GetRequiredService<TelegramLock>());
            provider
                .GetRequiredService<ITelegramLock>()
                .Should()
                .BeSameAs(provider.GetRequiredService<ITelegramLock>());
        }
    }

    [Fact]
    public void AddTelegramLock_Twice_ShouldRegisterOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramLock();

        // Act
        services.AddTelegramLock();

        // Assert
        services.Select(x => x.ServiceType).Should().BeEquivalentTo([typeof(TelegramLock), typeof(ITelegramLock)]);
    }
}
