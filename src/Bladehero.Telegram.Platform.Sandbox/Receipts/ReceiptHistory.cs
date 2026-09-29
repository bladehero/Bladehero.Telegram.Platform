using System.Globalization;
using System.Text;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// The receipts each member added, oldest first.
internal sealed class ReceiptHistory
{
    private readonly object _gate = new();
    private readonly Dictionary<long, List<Receipt>> _receipts = [];

    public void Add(Receipt receipt)
    {
        lock (_gate)
        {
            if (!_receipts.TryGetValue(receipt.OwnerId, out var receipts))
            {
                _receipts[receipt.OwnerId] = receipts = [];
            }

            receipts.Add(receipt);
        }
    }

    public IReadOnlyList<Receipt> Of(long userId)
    {
        lock (_gate)
        {
            return _receipts.TryGetValue(userId, out var receipts) ? [.. receipts] : [];
        }
    }

    public static string ToCsv(IEnumerable<Receipt> receipts)
    {
        var csv = new StringBuilder("date,total,points\n");
        foreach (var receipt in receipts)
        {
            csv.Append(
                CultureInfo.InvariantCulture,
                $"{receipt.Date:yyyy-MM-dd},{receipt.Total:0.00},{receipt.Points}\n"
            );
        }

        return csv.ToString();
    }
}
