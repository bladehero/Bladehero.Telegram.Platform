using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.History.InMemory;

/// <summary>Keeps the bot's history in memory: for development, tests and small bots.</summary>
public static class InMemoryHistoryDependencyInjection
{
    /// <summary>
    /// Keeps the history in memory, the latest <paramref name="maxEntriesPerChat"/> per chat; it's lost on restart.
    /// </summary>
    /// <param name="history">The history, from <c>AddTelegramHistory</c>.</param>
    /// <param name="maxEntriesPerChat">How many entries each chat keeps; entries without a chat share one such cap.</param>
    /// <returns><paramref name="history"/>, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxEntriesPerChat"/> is below 1.</exception>
    public static TelegramHistoryBuilder UseInMemory(this TelegramHistoryBuilder history, int maxEntriesPerChat = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntriesPerChat, 1);
        history.Services.AddSingleton<ITelegramHistoryStore>(new InMemoryTelegramHistoryStore(maxEntriesPerChat));
        return history;
    }
}
