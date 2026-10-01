namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A demo reader, with CoffeeShop:Demo on: every page reads as 12.40 EUR.
internal sealed class DemoReceiptReader : IReceiptReader
{
    public Task<ReceiptReading> ReadAsync(IReadOnlyList<ReceiptPage> pages, string? caption, CancellationToken token) =>
        Task.FromResult(ReceiptReading.Of(12.40m * pages.Count, "EUR"));
}
