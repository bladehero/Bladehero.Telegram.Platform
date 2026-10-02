using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History;

/// <summary>
/// Picks where the history is stored: <c>UseInMemory</c>, <c>UseEntityFrameworkCore</c>, or an
/// <see cref="ITelegramHistoryStore"/> of your own.
/// </summary>
public sealed class TelegramHistoryBuilder
{
    internal TelegramHistoryBuilder(IServiceCollection services) => Services = services;

    /// <summary>The app's services, where a store registers itself.</summary>
    public IServiceCollection Services { get; }
}
