using System.Runtime.CompilerServices;
using Bladehero.Telegram.Platform.History;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Testing;

// The bot's history: each action waits until it's stored, and reads include what the bot did so far.
public sealed partial class TelegramTestHost
{
    // Resolved once; holds null when the app doesn't record history.
    private StrongBox<TelegramHistoryWriter?>? _historyWriter;
    private ITelegramHistory? _history;

    /// <summary>The bot's history, for assertions; a read includes everything the bot did so far.</summary>
    /// <exception cref="InvalidOperationException">The app doesn't record history.</exception>
    public ITelegramHistory History =>
        _history ??= HistoryWriter is { } writer
            ? new TestHistory(Services.GetRequiredService<ITelegramHistory>(), writer)
            : throw new InvalidOperationException(
                "The bot doesn't record its history; register it with AddTelegramHistory()."
            );

    private TelegramHistoryWriter? HistoryWriter =>
        (_historyWriter ??= new(Services.GetService<TelegramHistoryWriter>())).Value;

    // Waits, within UpdateTimeout, until the calls in flight are recorded and everything recorded is stored.
    private async Task FlushHistoryAsync(CancellationToken token)
    {
        if (HistoryWriter is not { } writer)
        {
            return;
        }

        var flushing = writer.FlushAsync(waitForCalls: true, token);
        try
        {
            await (UpdateTimeout == Timeout.InfiniteTimeSpan ? flushing : flushing.WaitAsync(UpdateTimeout, token));
        }
        catch (TimeoutException) when (!flushing.IsCompleted)
        {
            throw new TimeoutException(
                $"The bot's history wasn't stored within {Describe(UpdateTimeout)}. Is its store stuck?"
            );
        }
    }

    // Reads that first wait for the calls in flight, so a test that saw a message arrive also sees its entry.
    private sealed class TestHistory(ITelegramHistory history, TelegramHistoryWriter writer) : ITelegramHistory
    {
        public async Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
            TelegramHistoryQuery query,
            CancellationToken token = default
        )
        {
            await writer.FlushAsync(waitForCalls: true, token);
            return await history.ReadAsync(query, token);
        }

        public Task FlushAsync(CancellationToken token = default) => writer.FlushAsync(waitForCalls: true, token);
    }
}
