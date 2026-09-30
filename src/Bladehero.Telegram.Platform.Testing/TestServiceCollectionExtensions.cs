using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>Test hooks for the bot's registrations.</summary>
public static class TestServiceCollectionExtensions
{
    /// <summary>Runs <paramref name="action"/> after the app is built, before any hosted service starts.</summary>
    /// <param name="services">The app's services, e.g. in <c>ConfigureTestServices</c>.</param>
    /// <param name="action">Gets the app's root services, e.g. to seed a database, and the start's token.</param>
    /// <remarks>
    /// Actions run in the order added. In a web app, its own code before <c>Run</c>, such as migrations, has already
    /// run. An exception fails the start.
    /// </remarks>
    public static IServiceCollection BeforeStart(
        this IServiceCollection services,
        Func<IServiceProvider, CancellationToken, Task> action
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(action);

        // First among the hosted services, after the actions added before.
        var after = -1;
        for (var index = 0; index < services.Count; index++)
        {
            // A keyed descriptor throws on ImplementationFactory.
            if (!services[index].IsKeyedService && services[index].ImplementationFactory?.Target is BeforeStartAction)
            {
                after = index;
            }
        }

        services.Insert(after + 1, ServiceDescriptor.Singleton<IHostedService>(new BeforeStartAction(action).Create));

        return services;
    }

    private sealed class BeforeStartAction(Func<IServiceProvider, CancellationToken, Task> action)
    {
        public IHostedService Create(IServiceProvider services) => new Service(services, action);
    }

    private sealed class Service(IServiceProvider services, Func<IServiceProvider, CancellationToken, Task> action)
        : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken) => action(services, cancellationToken);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
