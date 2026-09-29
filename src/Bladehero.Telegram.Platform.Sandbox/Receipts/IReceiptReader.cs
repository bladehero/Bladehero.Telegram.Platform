namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// Reads a receipt's total, e.g. by asking an AI model; the pages are photos or PDFs, in order.
internal interface IReceiptReader
{
    Task<ReceiptReading> ReadAsync(IReadOnlyList<ReceiptPage> pages, string? caption, CancellationToken token);
}

internal sealed record ReceiptPage(byte[] Content, string MediaType);

// The receipt's total, or why it could not be read.
internal sealed record ReceiptReading(decimal Total, string Currency, string? Error)
{
    public static ReceiptReading Of(decimal total, string currency) => new(total, currency, Error: null);

    public static ReceiptReading Failure(string error) => new(Total: 0, Currency: "", error);
}
