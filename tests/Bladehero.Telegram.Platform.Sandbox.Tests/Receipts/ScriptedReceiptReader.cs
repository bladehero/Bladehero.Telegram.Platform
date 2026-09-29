using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Sandbox.Receipts;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;

// Stands in for the AI reader: records what it was given, and answers with Reading or throws Failure.
internal sealed class ScriptedReceiptReader : IReceiptReader
{
    private readonly ConcurrentQueue<Read> _reads = new();

    public ReceiptReading Reading { get; init; } = ReceiptReading.Of(12.40m, "EUR");

    public Exception? Failure { get; init; }

    public IReadOnlyList<Read> Reads => [.. _reads];

    public Task<ReceiptReading> ReadAsync(IReadOnlyList<ReceiptPage> pages, string? caption, CancellationToken token)
    {
        _reads.Enqueue(new Read(pages, caption));
        return Failure is null ? Task.FromResult(Reading) : Task.FromException<ReceiptReading>(Failure);
    }

    internal sealed record Read(IReadOnlyList<ReceiptPage> Pages, string? Caption);
}
