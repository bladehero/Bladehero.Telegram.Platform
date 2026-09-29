using Bladehero.Telegram.Platform.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Sandbox.Tests;

internal static class SandboxBot
{
    // The coffee shop as Program composes it, with test settings and stand-ins registered last, so they win.
    public static Task<TelegramTestHost> StartAsync(
        FakeBotApi? api = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<HostApplicationBuilder>? configure = null
    ) =>
        TelegramTestHost.ForLongPollingAsync(
            builder =>
            {
                builder.Configuration.AddInMemoryCollection([new("TelegramReceiverConfiguration:Token", "unused")]);
                builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
                builder.Services.AddCoffeeShop(builder.Configuration);
                configure?.Invoke(builder);
            },
            api
        );
}
