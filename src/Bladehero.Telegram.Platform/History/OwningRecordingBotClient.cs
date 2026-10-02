using Telegram.Bot;

namespace Bladehero.Telegram.Platform.History;

// Wraps a disposable client built for it from the app's factory or type, which the container would have disposed.
internal sealed class OwningRecordingBotClient(ITelegramBotClient inner, TelegramHistoryWriter writer)
    : RecordingBotClient(inner, writer),
        IDisposable,
        IAsyncDisposable
{
    public void Dispose() => (Inner as IDisposable)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (Inner is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            Dispose();
        }
    }
}
