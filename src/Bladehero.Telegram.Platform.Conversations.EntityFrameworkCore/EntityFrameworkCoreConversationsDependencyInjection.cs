using Bladehero.Telegram.Platform.Receiving.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore;

/// <summary>Keeps the bot's conversations in a database through Entity Framework Core.</summary>
public static class EntityFrameworkCoreConversationsDependencyInjection
{
    /// <summary>
    /// Keeps conversations in <typeparamref name="TContext"/>'s database, written through a context instance of its
    /// own, apart from the app's changes.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TContext"/> maps them with <c>MapTelegramConversations</c>; startup fails when it doesn't,
    /// or when it isn't registered.
    /// </remarks>
    /// <typeparam name="TContext">The app's own context.</typeparam>
    /// <param name="conversations">The conversations, from <c>AddTelegramConversations</c>.</param>
    /// <returns><paramref name="conversations"/>, for chaining.</returns>
    public static TelegramConversationsBuilder UseEntityFrameworkCore<TContext>(
        this TelegramConversationsBuilder conversations
    )
        where TContext : DbContext
    {
        // Replaces the in-memory default, whichever was registered first.
        var services = conversations.Services;
        services.Replace(
            ServiceDescriptor.Singleton<IConversationStore>(
                provider => new EntityFrameworkCoreConversationStore<TContext>(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetService<TimeProvider>()
                )
            )
        );

        // Checked at start only, in a scope of its own.
        if (!services.Any(x => x.ServiceType == typeof(IValidateOptions<ContextCheck<TContext>>)))
        {
            var name = typeof(TContext).Name;
            services
                .AddOptions<ContextCheck<TContext>>()
                .Validate<IServiceProviderIsService>(
                    (_, registered) => registered.IsService(typeof(TContext)),
                    $"{name} isn't registered; add it with AddDbContext<{name}>()."
                )
                .Validate<IServiceScopeFactory>(
                    (_, scopes) => MapsTheConversations<TContext>(scopes),
                    $"{name} doesn't map the Telegram conversations: call modelBuilder.MapTelegramConversations() in "
                        + "its OnModelCreating, then add a migration."
                )
                .ValidateOnStart();
        }

        return conversations;
    }

    // An unregistered context is the other check's to report.
    private static bool MapsTheConversations<TContext>(IServiceScopeFactory scopes)
        where TContext : DbContext
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetService<TContext>() is not { } context
            || context.Model.FindEntityType(typeof(StoredConversation)) is not null;
    }

    // Carries the startup checks of TContext.
    private sealed class ContextCheck<TContext>;
}
