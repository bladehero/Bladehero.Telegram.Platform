using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class HistoryDependencyInjectionTests
{
    [Fact]
    public async Task AddTelegramHistory_WithoutAStore_ShouldFailToStartNamingTheChoices()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory();
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should()
            .ContainAll("UseInMemory()", "UseEntityFrameworkCore<TContext>()", "ITelegramHistoryStore");
    }

    [Fact]
    public async Task AddTelegramHistory_WithAQueueCapacityBelowOne_ShouldFailToStart()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramHistory(x => x.QueueCapacity = 0).Services.AddSingleton(Store());
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("Telegram history QueueCapacity must be at least 1.");
    }

    [Fact]
    public void AddTelegramHistory_Twice_ShouldRunOneWriter()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        services.AddTelegramHistory().Services.AddSingleton(Store());

        // Act
        services.AddTelegramHistory();

        // Assert
        using var provider = services.BuildServiceProvider();
        provider
            .GetServices<IHostedService>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TelegramHistoryWriter>();
    }

    [Fact]
    public void AddTelegramHistory_ShouldReturnABuilderOverTheSameServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var history = services.AddTelegramHistory();

        // Assert
        history.Services.Should().BeSameAs(services);
    }

    private static ITelegramHistoryStore Store() => Mock.Of<ITelegramHistoryStore>();
}
