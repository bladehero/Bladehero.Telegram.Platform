using Bladehero.Telegram.Platform.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

// Starts the webhook sandbox as it runs in production, with the configuration its user secrets would hold.
internal static class WebhookBot
{
    public const string BaseUrl = "https://bot.example.com";

    public static Task<TelegramTestHost> StartAsync(
        string baseUrl = BaseUrl,
        FakeBotApi? api = null,
        Action<IWebHostBuilder>? configure = null
    ) =>
        TelegramTestHost.ForWebhookAsync<Program>(
            web =>
            {
                web.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["TelegramWebhookConfiguration:Token"] = "unused",
                                ["TelegramWebhookConfiguration:BaseUrl"] = baseUrl,
                                ["TelegramWebhookConfiguration:UpdateEndpoint"] = "telegram/updates",
                            }
                        )
                );
                configure?.Invoke(web);
            },
            api
        );
}
