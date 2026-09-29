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

    [Fact]
    public void TheBotClientIsResolvableAndIsTheOneTheLibraryUses()
    {
        using var provider = Build(FromConfiguration());

        var client = provider.GetRequiredService<ITelegramBotClient>();

        Assert.Equal(1234567, client.BotId);
        Assert.Same(client, provider.GetRequiredService<TelegramBotClientAccessor>().Client);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClientTheAppRegistersIsTheOneTheLibraryUsesWhicheverOrderItIsRegisteredIn(bool registeredFirst)
    {
        var own = new TelegramBotClient("7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw");
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

        Assert.Same(own, provider.GetRequiredService<ITelegramBotClient>());
        Assert.Same(own, provider.GetRequiredService<TelegramBotClientAccessor>().Client);
    }

    [Fact]
    public void TheSenderIsResolvableForMessagesTheBotStartsItself()
    {
        using var provider = Build(FromConfiguration());

        Assert.NotNull(provider.GetService<ITelegramSender>());
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
        Assert.NotNull(provider.GetService<ITelegramSender>());
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
        Assert.NotNull(provider.GetService<ITelegramSender>());
    }

    [Fact]
    public void TheTokenFromConfigurationReachesTheClient()
    {
        using var provider = Build(FromConfiguration());

        var accessor = provider.GetRequiredService<TelegramBotClientAccessor>();

        Assert.Equal(1234567, accessor.Client.BotId);
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
        var accessor = provider.GetRequiredService<TelegramBotClientAccessor>();

        Assert.Equal(7654321, accessor.Client.BotId);
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
        var accessor = provider.GetRequiredService<TelegramBotClientAccessor>();

        Assert.Equal(7654321, accessor.Client.BotId);
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
