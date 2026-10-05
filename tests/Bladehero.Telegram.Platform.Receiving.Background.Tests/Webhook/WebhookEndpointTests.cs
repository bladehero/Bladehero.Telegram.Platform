using System.Net;
using System.Text;
using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Timeouts;
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

    [Fact]
    public async Task TheWebhookRecordsTheUpdateAndLinksAnErrorHandlersReplyToIt()
    {
        var store = new HistoryStore();
        var log = new ScopeLog { CommandFailure = new InvalidOperationException("original"), Apology = "Sorry" };
        await using var app = await StartAsync(
            log,
            new LogRecorder(),
            services =>
            {
                services.AddScoped<ITelegramErrorHandler, ProbeErrorHandler>();
                services.AddTelegramHistory().Services.AddSingleton<ITelegramHistoryStore>(store);
            }
        );
        using var client = app.GetTestClient();

        using var response = await client.PostAsync(
            "/telegram/updates",
            new StringContent(PrivateUpdate, Encoding.UTF8, "application/json")
        );
        await app.Services.GetRequiredService<ITelegramHistory>().FlushAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var linked = store.Entries.Where(entry => entry.UpdateId == 7).ToList();
        Assert.Equal(["message", "sendMessage"], linked.Select(entry => entry.Kind));
        Assert.Equal("Sorry", linked[1].Text);
    }

    [Fact]
    public async Task WithTheLockAnUpdatePostedWhileAnotherIsHandledWaitsForIt()
    {
        var log = new ScopeLog();
        var first = log.Gates[1] = new UpdateGate();
        await using var app = await StartAsync(log, new LogRecorder(), services => services.AddTelegramLock());
        var telegramLock = app.Services.GetRequiredService<ITelegramLock>();
        using var client = app.GetTestClient();
        var firstResponse = client.PostAsync("/telegram/updates", Numbered(1));
        await first.Reached.WaitAsync(Patience);

        var secondResponse = client.PostAsync("/telegram/updates", Numbered(2));
        await telegramLock.WaitForWaitersAsync(1);
        var askedWhileTheFirstWasHandled = log.CommandInstances.Count;
        first.Open();
        var responses = await Task.WhenAll(firstResponse, secondResponse).WaitAsync(Patience);

        Assert.Equal(1, askedWhileTheFirstWasHandled);
        Assert.Equal(2, log.SeenByUpdates.Count);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    [Fact]
    public async Task WithoutTheLockUpdatesPostedAtOnceAreHandledAtOnce()
    {
        var log = new ScopeLog();
        var first = log.Gates[1] = new UpdateGate();
        await using var app = await StartAsync(log, new LogRecorder());
        using var client = app.GetTestClient();
        var firstResponse = client.PostAsync("/telegram/updates", Numbered(1));
        await first.Reached.WaitAsync(Patience);

        using var second = await client.PostAsync("/telegram/updates", Numbered(2)).WaitAsync(Patience);
        var firstStillHandled = !firstResponse.IsCompleted;
        first.Open();
        using var firstDone = await firstResponse.WaitAsync(Patience);

        Assert.True(firstStillHandled);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Null(app.Services.GetService<ITelegramLock>());
    }

    [Fact]
    public async Task WithTheLockAnUpdateWhoseRequestIsCancelledWhileItWaitsGets503AndIsNotHandled()
    {
        var log = new ScopeLog();
        var logs = new LogRecorder();
        using var cancelRequest = new CancellationTokenSource();
        await using var app = await StartAsync(
            log,
            logs,
            services => services.AddTelegramLock(),
            pipeline =>
                pipeline.Use(
                    (context, next) =>
                    {
                        // Cancelled by the test with the connection still open, as a request timeout does.
                        context.RequestAborted = cancelRequest.Token;
                        return next(context);
                    }
                )
        );
        var telegramLock = app.Services.GetRequiredService<ITelegramLock>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = telegramLock.RunAsync((_, _) => release.Task);
        using var client = app.GetTestClient();
        var response = client.PostAsync("/telegram/updates", Numbered(1));
        await telegramLock.WaitForWaitersAsync(1);

        await cancelRequest.CancelAsync();
        using var answered = await response.WaitAsync(Patience);
        release.SetResult();
        await work;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answered.StatusCode);
        Assert.Empty(log.CommandInstances);
        Assert.Empty(log.SeenByErrors);
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equal(0, telegramLock.WaitingCount);
    }

    [Fact]
    public async Task WithTheLockAnUpdateWhoseRequestTimesOutWhileItWaitsGets503AndIsNotHandled()
    {
        var log = new ScopeLog();
        await using var app = await StartAsync(
            log,
            new LogRecorder(),
            services =>
                services
                    .AddTelegramLock()
                    .AddRequestTimeouts(options =>
                        options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromMilliseconds(100) }
                    ),
            pipeline => pipeline.UseRequestTimeouts()
        );
        var telegramLock = app.Services.GetRequiredService<ITelegramLock>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = telegramLock.RunAsync((_, _) => release.Task);
        using var client = app.GetTestClient();

        using var answered = await client.PostAsync("/telegram/updates", Numbered(1)).WaitAsync(Patience);
        release.SetResult();
        await work;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answered.StatusCode);
        Assert.Empty(log.CommandInstances);
        Assert.Empty(log.SeenByErrors);
    }

    [Fact]
    public async Task WithTheLockAnUpdateWaitingAsTheAppBeginsToStopIsStillHandled()
    {
        var log = new ScopeLog();
        await using var app = await StartAsync(log, new LogRecorder(), services => services.AddTelegramLock());
        var telegramLock = app.Services.GetRequiredService<ITelegramLock>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = telegramLock.RunAsync((_, _) => release.Task);
        using var client = app.GetTestClient();
        var response = client.PostAsync("/telegram/updates", Numbered(1));
        await telegramLock.WaitForWaitersAsync(1);

        app.Lifetime.StopApplication();
        release.SetResult();
        using var answered = await response.WaitAsync(Patience);
        await work;

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Single(log.SeenByUpdates);
        Assert.Empty(log.SeenByErrors);
    }

    // Only bounds a failing test's wait; not a sleep.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // A private message, as update `id`.
    private static StringContent Numbered(int id) =>
        new(
            $$$"""{"update_id":{{{id}}},"message":{"message_id":{{{id}}},"date":0,"chat":{"id":42,"type":"private"},"text":"hi"}}""",
            Encoding.UTF8,
            "application/json"
        );

    private const string PrivateUpdate = """
        {"update_id":7,"message":{"message_id":1,"date":1700000000,"text":"hi",
         "chat":{"id":7000000001,"type":"private","first_name":"Nick"},
         "from":{"id":7000000001,"is_bot":false,"first_name":"Nick"}}}
        """;

    // The ScopeLog's command and a ThrowingErrorHandler, behind UseTelegramWebhook; startup talks to a fake client.
    // configure then changes the app's services, e.g. to add history after that client, and pipeline adds middleware in
    // front of the webhook.
    private static async Task<WebApplication> StartAsync(
        ScopeLog log,
        LogRecorder logs,
        Action<IServiceCollection>? configure = null,
        Action<WebApplication>? pipeline = null
    )
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
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        pipeline?.Invoke(app);
        app.UseTelegramWebhook();
        await app.StartAsync();
        return app;
    }
}
