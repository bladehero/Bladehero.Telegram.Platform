using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.History;

// The update being handled, for the calls made while handling it; null outside updates. Work the update starts but
// doesn't await inherits it, unless started with ExecutionContext.SuppressFlow().
internal static class TelegramHistoryCause
{
    private static readonly AsyncLocal<Update?> CurrentUpdate = new();

    internal static Update? Current => CurrentUpdate.Value;

    // Dispose restores the previous update.
    internal static IDisposable Begin(Update update)
    {
        var previous = CurrentUpdate.Value;
        CurrentUpdate.Value = update;
        return new Scope(previous);
    }

    private sealed class Scope(Update? previous) : IDisposable
    {
        public void Dispose() => CurrentUpdate.Value = previous;
    }
}
