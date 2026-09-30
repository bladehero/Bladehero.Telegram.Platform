using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class TelegramBotIdentityInitializerTests
{
    private const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Theory]
    [InlineData("long polling")]
    [InlineData("webhook")]
    public void TheIdentityInitializerLearnsTheUsernameBeforeTheOtherInitializersRun(string receiving)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        Receive(services, receiving);
        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToArray();

        Assert.IsType<TelegramBotIdentityInitializer>(hosted[0]);
    }

    [Fact]
    public async Task AFailedGetMeAtStartupLogsAWarningAndTheBotStillStarts()
    {
        var logger = new MessageLogger();
        var sut = new TelegramBotIdentityInitializer(new ClientIdentity(new FakeBotClient(unreachable: true)), logger);

        await sut.StartingAsync(CancellationToken.None);

        var (level, message, exception) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal(
            "Couldn't learn the bot's username with getMe; commands addressed to a bot (/start@name) are accepted for "
                + "any name until it can.",
            message
        );
        Assert.IsType<HttpRequestException>(exception);
    }

    [Fact]
    public async Task TheWebhookInitializerReusesTheIdentitySoAWebhookBotMakesOneGetMe()
    {
        var client = new FakeBotClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        Receive(services, "webhook");
        services.AddSingleton<ITelegramBotClient>(client);
        await using var provider = services.BuildServiceProvider();

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(CancellationToken.None);
        }

        Assert.Equal(["getMe", "getWebhookInfo"], client.Requests.Take(2));
        Assert.Contains("setWebhook", client.Requests);
        Assert.Single(client.Requests, x => x == "getMe");
    }

    private static void Receive(IServiceCollection services, string receiving)
    {
        if (receiving == "webhook")
        {
            services.AddTelegramWebhookReceiving(
                webhook =>
                {
                    webhook.Token = Token;
                    webhook.BaseUrl = "https://bot.example.com";
                    webhook.UpdateEndpoint = "telegram/updates";
                },
                typeof(ProbeCommand).Assembly
            );
        }
        else
        {
            services.AddTelegramLongPollingReceiving(receiver => receiver.Token = Token, typeof(ProbeCommand).Assembly);
        }
    }

    // Keeps each entry's level, formatted message and exception.
    private sealed class MessageLogger : ILogger<TelegramBotIdentityInitializer>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
