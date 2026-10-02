using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.History;

// Wraps the bot's ITelegramBotClient registrations so its calls are recorded.
internal static class TelegramHistoryClients
{
    // Wraps each non-keyed client registered so far, once; a no-op without history. Keyed clients are other bots'.
    internal static void Decorate(IServiceCollection services)
    {
        if (!services.Any(x => x.ServiceType == typeof(TelegramHistoryWriter)))
        {
            return;
        }

        for (var i = 0; i < services.Count; i++)
        {
            if (IsClient(services[i]) && !IsRecording(services[i]))
            {
                services[i] = new ServiceDescriptor(
                    typeof(ITelegramBotClient),
                    new Decorator(services[i]).Create,
                    services[i].Lifetime
                );
            }
        }
    }

    // Whether the registration is the bot's client, wrapped to record its calls.
    internal static bool IsRecording(ServiceDescriptor descriptor) =>
        IsClient(descriptor) && descriptor.ImplementationFactory?.Target is Decorator;

    private static bool IsClient(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(ITelegramBotClient) && !descriptor.IsKeyedService;

    private sealed class Decorator(ServiceDescriptor inner)
    {
        // An instance stays the app's; a client built from a factory or a type is the wrapper's to dispose.
        public object Create(IServiceProvider provider)
        {
            var (client, owned) = inner switch
            {
                { ImplementationInstance: ITelegramBotClient instance } => (instance, false),
                { ImplementationFactory: { } factory } => ((ITelegramBotClient)factory(provider), true),
                _ => ((ITelegramBotClient)ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!), true),
            };
            var writer = provider.GetRequiredService<TelegramHistoryWriter>();
            return owned && client is IDisposable or IAsyncDisposable
                ? new OwningRecordingBotClient(client, writer)
                : new RecordingBotClient(client, writer);
        }
    }
}
