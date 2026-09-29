using System.Collections.Concurrent;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// Receipts read and shown on a card, waiting for Add or Discard. Kept in memory, so a restart loses them.
internal sealed class PendingReceipts
{
    private readonly ConcurrentDictionary<string, Receipt> _receipts = new();

    // A short id, so the card's buttons stay within Telegram's 64 bytes of callback data.
    public string Add(Receipt receipt)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        _receipts[id] = receipt;
        return id;
    }

    // Only one of two taps at once gets the receipt.
    public Receipt? Take(string id) => _receipts.TryRemove(id, out var receipt) ? receipt : null;
}
