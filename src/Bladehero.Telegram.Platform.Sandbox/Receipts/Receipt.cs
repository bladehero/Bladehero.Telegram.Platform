using System.Globalization;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// A receipt read for a member, worth a point for each whole unit of its total.
internal sealed record Receipt(long OwnerId, DateTime Date, decimal Total, string Currency)
{
    public int Points => (int)Math.Floor(Total);

    public string Amount => string.Create(CultureInfo.InvariantCulture, $"{Total:0.00} {Currency}");
}
