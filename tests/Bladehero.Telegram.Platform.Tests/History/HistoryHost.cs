using Bladehero.Telegram.Platform.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Tests.History;

// An app with history over a RecordingStore, a FakeTimeProvider and recorded logs.
internal sealed class HistoryHost : IAsyncDisposable
{
    public const long ChatId = -1001234567890;

    private HistoryHost(IHost host, RecordingStore store, LogRecorder logs, FakeTimeProvider time)
    {
        Host = host;
        Store = store;
        Logs = logs;
        Time = time;
    }

    public IHost Host { get; }

    public RecordingStore Store { get; }

    public LogRecorder Logs { get; }

    public FakeTimeProvider Time { get; }

    public TelegramHistoryWriter Writer => Host.Services.GetRequiredService<TelegramHistoryWriter>();

    public ITelegramHistory History => Host.Services.GetRequiredService<ITelegramHistory>();

    public static TelegramHistoryEntry Entry(int n) =>
        new()
        {
            Kind = "message",
            ChatId = ChatId,
            Text = $"#{n}",
        };

    public static async Task<HistoryHost> StartAsync(
        Action<TelegramHistoryOptions>? configure = null,
        Action<IServiceCollection>? services = null
    )
    {
        var host = Build(configure, services);
        await host.Host.StartAsync();
        return host;
    }

    // Not started.
    public static HistoryHost Build(
        Action<TelegramHistoryOptions>? configure = null,
        Action<IServiceCollection>? services = null
    )
    {
        var store = new RecordingStore();
        var logs = new LogRecorder();
        var time = new FakeTimeProvider();
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new());
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddTelegramHistory(configure).Services.AddSingleton<ITelegramHistoryStore>(store);
        services?.Invoke(builder.Services);
        return new HistoryHost(builder.Build(), store, logs, time);
    }

    public async ValueTask DisposeAsync()
    {
        Store.Open();
        await Host.StopAsync();
        Host.Dispose();
    }
}
