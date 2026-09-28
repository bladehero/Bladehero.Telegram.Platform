using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal static class DependencyInjection
{
    internal static void AddTelegramConversations(this IServiceCollection services)
    {
        services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddScoped<Conversation>();
        services.AddScoped<IConversation>(provider => provider.GetRequiredService<Conversation>());
    }
}
