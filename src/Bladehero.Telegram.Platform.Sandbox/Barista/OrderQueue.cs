using System.Threading.Channels;
using Bladehero.Telegram.Platform.Sandbox.Coffee;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Sandbox.Barista;

// Placed orders, for the barista to announce in turn. Each starts brewing as it is queued, within the update that
// placed it, so it is ready BrewTime later however soon the barista gets to it, and a test's clock can be moved on
// as soon as the order is confirmed.
internal sealed class OrderQueue(
    TimeProvider time,
    IOptions<CoffeeShopOptions> options,
    IHostApplicationLifetime lifetime
)
{
    private readonly Channel<BrewingOrder> _orders = Channel.CreateUnbounded<BrewingOrder>();

    public void Queue(PlacedOrder order) =>
        _orders.Writer.TryWrite(
            new BrewingOrder(order, Task.Delay(options.Value.BrewTime, time, lifetime.ApplicationStopping))
        );

    public IAsyncEnumerable<BrewingOrder> ReadAllAsync(CancellationToken token) => _orders.Reader.ReadAllAsync(token);
}

internal sealed record PlacedOrder(long ChatId, CoffeeSize Size, string CupName);

internal sealed record BrewingOrder(PlacedOrder Order, Task Brewed);
