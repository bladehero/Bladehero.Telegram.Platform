using System.Net;
using System.Text;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests.Webhook;

// The real endpoint on an in-memory server, with no test host in between to catch what the app's handlers throw.
public sealed class WebhookEndpointTests
{
    private const string Update =
        """{"update_id":7,"message":{"message_id":1,"date":0,"chat":{"id":42,"type":"private"},"text":"hi"}}""";

    [Fact]
    public async Task AThrowingErrorHandlerIsLoggedAndTheUpdateStillAnswered200()
    {
        var logs = new LogRecorder();
        var log = new ScopeLog { CommandFailure = new InvalidOperationException("original") };
        await using var app = await StartAsync(log, logs);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(
            "/telegram/updates",
            new StringContent(Update, Encoding.UTF8, "application/json")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logged = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        var both = Assert.IsType<AggregateException>(logged.Exception);
        Assert.Collection(
            both.InnerExceptions,
            error => Assert.Same(log.CommandFailure, error),
            failure => Assert.Equal("boom", failure.Message)
        );
    }

    [Fact]
    public async Task TheWebhookReportsAFailedUpdateInsideItsLogScope()
    {
        var logs = new LogRecorder();
        var log = new ScopeLog { CommandFailure = new InvalidOperationException("original") };
        await using var app = await StartAsync(log, logs);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(
            "/telegram/updates",
            new StringContent(Update, Encoding.UTF8, "application/json")
        );

        Assert.Equal(7, Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error).UpdateId);
    }

    // The ScopeLog's command and a ThrowingErrorHandler, behind UseTelegramWebhook; startup talks to a fake client.
    private static async Task<WebApplication> StartAsync(ScopeLog log, LogRecorder logs)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs);

        builder.Services.AddSingleton(log);
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddTelegramWebhookReceiving(
            webhook =>
            {
                webhook.Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
                webhook.BaseUrl = "https://bot.example.com";
                webhook.UpdateEndpoint = "telegram/updates";
            },
            typeof(ProbeCommand).Assembly
        );
        builder.Services.Replace(ServiceDescriptor.Singleton<ITelegramBotClient>(new FakeBotClient()));
        builder.Services.AddScoped<ITelegramErrorHandler, ThrowingErrorHandler>();

        var app = builder.Build();
        app.UseTelegramWebhook();
        await app.StartAsync();
        return app;
    }
}
