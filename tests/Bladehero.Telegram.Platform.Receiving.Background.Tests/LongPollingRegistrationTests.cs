using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class LongPollingRegistrationTests
{
    private const string Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Fact]
    public void TheUpdateHandlerUsedByThePollingLoopIsTheScopingOne()
    {
        var services = FromConfiguration();

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(ScopedUpdateHandler));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void TheLibraryRegistersNoTimeProvider()
    {
        var services = FromConfiguration();

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void TheWebhookIsClearedBeforeThePollingLoopStarts()
    {
        using var provider = Build(FromConfiguration());

        var hosted = provider.GetServices<IHostedService>();

        Assert.Single(hosted.OfType<TelegramLongPollingInitializer>());
    }

    [Fact]
    public void TheCommandMenuIsSyncedWhenPollingStarts()
    {
        using var provider = Build(FromConfiguration());

        var hosted = provider.GetServices<IHostedService>();

        Assert.Single(hosted.OfType<TelegramCommandMenuInitializer<TelegramReceiverConfiguration>>());
    }

    [Fact]
    public void TheCommandMenuIsSyncedWhenTheWebhookStarts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramWebhookReceiving(
            configuration =>
            {
                configuration.Token = Token;
                configuration.BaseUrl = "https://bot.example.com";
                configuration.UpdateEndpoint = "telegram/updates";
            },
            typeof(ProbeCommand).Assembly
        );

        using var provider = Build(services);

        var hosted = provider.GetServices<IHostedService>();

        Assert.Single(hosted.OfType<TelegramCommandMenuInitializer<TelegramWebhookConfiguration>>());
    }

    [Fact]
    public void TheLongPollingHostedServiceResolves()
    {
        using var provider = Build(FromConfiguration());

        var hosted = provider.GetServices<IHostedService>();

        Assert.Single(hosted.OfType<TelegramLongPollingBackgroundService>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AClientTheAppRegistersIsTheOneTheLibraryUsesWhicheverOrderItIsRegisteredIn(bool registeredFirst)
    {
        var own = new FakeBotClient();
        var services = new ServiceCollection();

        if (registeredFirst)
        {
            services.AddSingleton<ITelegramBotClient>(own);
        }

        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramLongPollingReceiving(
            configuration => configuration.Token = Token,
            typeof(ProbeCommand).Assembly
        );

        if (!registeredFirst)
        {
            services.AddSingleton<ITelegramBotClient>(own);
        }

        using var provider = Build(services);
        var initializer = Assert.Single(
            provider.GetServices<IHostedService>().OfType<TelegramLongPollingInitializer>()
        );

        await initializer.StartingAsync(CancellationToken.None);

        Assert.Same(own, provider.GetRequiredService<ITelegramBotClient>());
        Assert.Contains("getWebhookInfo", own.Requests);
    }

    [Fact]
    public void TheMessagesAreResolvableForMessagesTheBotStartsItself()
    {
        using var provider = Build(FromConfiguration());

        Assert.NotNull(provider.GetService<ITelegramMessages>());
    }

    [Fact]
    public void TheActionOverloadWiresUpTheBotToo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramLongPollingReceiving(
            configuration => configuration.Token = Token,
            typeof(ProbeCommand).Assembly
        );

        using var provider = Build(services);

        Assert.Single(provider.GetServices<IHostedService>().OfType<TelegramLongPollingBackgroundService>());
        Assert.NotNull(provider.GetService<ITelegramMessages>());
    }

    [Fact]
    public void TheWebhookActionOverloadWiresUpTheBotToo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramWebhookReceiving(
            configuration =>
            {
                configuration.Token = Token;
                configuration.BaseUrl = "https://bot.example.com";
                configuration.UpdateEndpoint = "telegram/updates";
            },
            typeof(ProbeCommand).Assembly
        );

        using var provider = Build(services);

        Assert.Single(provider.GetServices<IHostedService>().OfType<TelegramWebhookInitializer>());
        Assert.NotNull(provider.GetService<ITelegramMessages>());
    }

    [Fact]
    public void TheTokenFromConfigurationReachesTheClient()
    {
        using var provider = Build(FromConfiguration());

        var client = provider.GetRequiredService<ITelegramBotClient>();

        Assert.Equal(1234567, client.BotId);
    }

    [Fact]
    public void TheTokenFromTheActionOverloadReachesTheClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramLongPollingReceiving(
            configuration => configuration.Token = "7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw",
            typeof(ProbeCommand).Assembly
        );

        using var provider = Build(services);
        var client = provider.GetRequiredService<ITelegramBotClient>();

        Assert.Equal(7654321, client.BotId);
    }

    [Fact]
    public void TheTokenFromTheWebhookActionOverloadReachesTheClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramWebhookReceiving(
            configuration =>
            {
                configuration.Token = "7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";
                configuration.BaseUrl = "https://bot.example.com";
                configuration.UpdateEndpoint = "telegram/updates";
            },
            typeof(ProbeCommand).Assembly
        );

        using var provider = Build(services);
        var client = provider.GetRequiredService<ITelegramBotClient>();

        Assert.Equal(7654321, client.BotId);
    }

    private static IServiceCollection FromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TelegramReceiverConfiguration:Token"] = Token })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ScopeLog());
        services.AddScoped<ScopedDependency>();
        services.AddTelegramLongPollingReceiving(configuration, assemblies: typeof(ProbeCommand).Assembly);
        return services;
    }

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
}
