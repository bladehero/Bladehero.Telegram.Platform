using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TestServiceCollectionExtensionsTests
{
    [Fact]
    public async Task BeforeStart_ShouldRunBeforeTheFirstPoll()
    {
        // Arrange
        var api = new FakeBotApi();
        int? pollsBefore = null;

        // Act
        await using var bot = await TestBot.StartAsync(
            api,
            services: services =>
                services.BeforeStart(
                    async (provider, token) =>
                    {
                        pollsBefore = api.Polls;
                        await provider.GetRequiredService<ITelegramBotClient>().GetMe(token);
                    }
                )
        );

        // Assert
        using (new AssertionScope())
        {
            pollsBefore.Should().Be(0);
            api.Calls[0].Method.Should().Be("getMe");
        }
    }

    [Fact]
    public async Task BeforeStart_Twice_ShouldRunInTheOrderAdded()
    {
        // Arrange
        var order = new List<string>();

        // Act
        await using var bot = await TestBot.StartAsync(services: services =>
        {
            services.AddHostedService(_ => new StartingRecorder(order));
            services.BeforeStart(Record(order, "first"));
            services.BeforeStart(Record(order, "second"));
        });

        // Assert
        order.Should().Equal("first", "second", "the app's own");
    }

    [Fact]
    public async Task BeforeStart_WhenItThrows_ShouldFailTheStartWithItsException()
    {
        // Arrange
        var api = new FakeBotApi();

        // Act
        var act = () =>
            TestBot.StartAsync(
                api,
                services: services =>
                    services.BeforeStart((_, _) => throw new InvalidOperationException("The seed data is missing"))
            );

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The seed data is missing");
            api.Polls.Should().Be(0);
        }
    }

    private static Func<IServiceProvider, CancellationToken, Task> Record(List<string> order, string name) =>
        (_, _) =>
        {
            order.Add(name);
            return Task.CompletedTask;
        };

    // An app's own service that starts early.
    private sealed class StartingRecorder(List<string> order) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            order.Add("the app's own");
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
