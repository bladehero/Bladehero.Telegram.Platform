using Bladehero.Telegram.Platform.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public enum BotMode
{
    Webhook,
    LongPolling,
}

internal static class SandboxBot
{
    public const string BaseUrl = "https://bot.example.com";

    public static Task<TelegramTestHost> StartAsync(
        BotMode mode,
        FakeBotApi? api = null,
        Action<IWebHostBuilder>? configure = null,
        string baseUrl = BaseUrl
    ) =>
        mode == BotMode.Webhook
            ? TelegramTestHost.ForWebhookAsync<Program>(web => Configure(web, mode, baseUrl, configure), api)
            : TelegramTestHost.ForLongPollingAsync<Program>(web => Configure(web, mode, baseUrl, configure), api);

    // The app reads its mode before Build, which UseSetting reaches and ConfigureAppConfiguration does not.
    public static void Configure(
        IWebHostBuilder web,
        BotMode mode,
        string baseUrl = BaseUrl,
        Action<IWebHostBuilder>? configure = null
    )
    {
        web.UseSetting("Telegram:Token", "unused");

        // A key the test does not set comes from the developer's user secrets; `configure` may set one.
        web.UseSetting("Telegram:SecretToken", "");

        if (mode == BotMode.Webhook)
        {
            web.UseSetting("Telegram:BaseUrl", baseUrl);
            web.UseSetting("Telegram:UpdateEndpoint", "telegram/updates");
        }
        else
        {
            // A key the test does not set comes from the developer's user secrets.
            web.UseSetting("Telegram:BaseUrl", "");
            web.UseSetting("Telegram:UpdateEndpoint", "");
        }

        configure?.Invoke(web);
    }
}
