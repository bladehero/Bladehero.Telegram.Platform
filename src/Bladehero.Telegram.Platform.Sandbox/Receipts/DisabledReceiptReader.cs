namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// The default until a real reader is registered: the bot still answers, and says why it cannot read.
internal sealed class DisabledReceiptReader : IReceiptReader
{
    public Task<ReceiptReading> ReadAsync(IReadOnlyList<ReceiptPage> pages, string? caption, CancellationToken token) =>
        Task.FromResult(ReceiptReading.Failure("Receipt reading is not set up."));
}
