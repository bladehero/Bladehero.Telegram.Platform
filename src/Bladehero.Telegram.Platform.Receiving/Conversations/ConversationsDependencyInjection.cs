using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Registers the bot's conversations; the receiving setups call it, so an app calls it to pick their store.
/// </summary>
public static class ConversationsDependencyInjection
{
    /// <summary>
    /// Registers conversations, kept in memory unless a store is picked next, and returns a builder to pick one.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <returns>A builder to pick the store with.</returns>
    public static TelegramConversationsBuilder AddTelegramConversations(this IServiceCollection services)
    {
        services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
        services.TryAddSingleton<ConversationLocks>();

        // Once per container, however often it's called.
        if (!services.Any(x => x.ServiceType == typeof(Conversation)))
        {
            services.AddScoped<Conversation>();
            services.AddScoped<IConversation>(provider => provider.GetRequiredService<Conversation>());
        }

        return new TelegramConversationsBuilder(services);
    }
}
