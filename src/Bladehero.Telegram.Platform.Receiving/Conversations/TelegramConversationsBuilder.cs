using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>
/// Picks where conversations are kept: <c>UseEntityFrameworkCore</c>, or an <see cref="IConversationStore"/> of your
/// own.
/// </summary>
public sealed class TelegramConversationsBuilder
{
    internal TelegramConversationsBuilder(IServiceCollection services) => Services = services;

    /// <summary>The app's services, where a store registers itself.</summary>
    public IServiceCollection Services { get; }
}
