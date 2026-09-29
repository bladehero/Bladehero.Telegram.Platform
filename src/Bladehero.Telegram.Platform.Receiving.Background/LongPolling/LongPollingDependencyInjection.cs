using System.Reflection;
using Bladehero.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

/// <summary>Hosts the bot by long polling: it pulls its updates, so no public URL is needed.</summary>
public static class LongPollingDependencyInjection
{
    /// <summary>
    /// Receives updates by long polling for the commands in <paramref name="assemblies"/>, with
    /// <see cref="TelegramReceiverConfiguration"/> bound from <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// Registers the bot as <c>AddTelegramBot</c> does and the commands as <c>AddTelegramReceiving</c> does. A bot
    /// can't poll while it has a webhook, so startup deletes one if found.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configuration">The configuration that holds the receiver's section.</param>
    /// <param name="sectionName">
    /// The section to bind; when <c>null</c>, the one named after the type: <c>TelegramReceiverConfiguration</c>.
    /// </param>
    /// <param name="httpClientFactory">
    /// Builds the <see cref="HttpClient"/> of the client the library builds, e.g. for a proxy; it doesn't apply to a
    /// client the app registers itself.
    /// </param>
    /// <param name="assemblies">The assemblies to scan for commands.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddTelegramLongPollingReceiving(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null,
        params Assembly[] assemblies
    )
    {
        services.AddConfiguration<TelegramReceiverConfiguration>(configuration, sectionName);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling for the commands in <paramref name="assemblies"/>, with
    /// <see cref="TelegramReceiverConfiguration"/> set by <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// Registers the bot as <c>AddTelegramBot</c> does and the commands as <c>AddTelegramReceiving</c> does. A bot
    /// can't poll while it has a webhook, so startup deletes one if found.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets the configuration, e.g. its token.</param>
    /// <param name="assemblies">The assemblies to scan for commands.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddTelegramLongPollingReceiving(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration> configure,
        params Assembly[] assemblies
    )
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling like <see cref="AddTelegramLongPollingReceiving(IServiceCollection,
    /// Action{TelegramReceiverConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> resolved from the
    /// container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling like <see cref="AddTelegramLongPollingReceiving(IServiceCollection,
    /// Action{TelegramReceiverConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> and
    /// <typeparamref name="TDep2"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling like <see cref="AddTelegramLongPollingReceiving(IServiceCollection,
    /// Action{TelegramReceiverConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep3"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling like <see cref="AddTelegramLongPollingReceiving(IServiceCollection,
    /// Action{TelegramReceiverConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep4"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3, TDep4> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    /// <summary>
    /// Receives updates by long polling like <see cref="AddTelegramLongPollingReceiving(IServiceCollection,
    /// Action{TelegramReceiverConfiguration}, Assembly[])"/>, with <typeparamref name="TDep1"/> to
    /// <typeparamref name="TDep5"/> resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramLongPollingReceiving<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramReceiverConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure,
        params Assembly[] assemblies
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramReceiverConfiguration>().Configure(configure);
        services.AddTelegramLongPollingReceivingCore(httpClientFactory: null, assemblies);
        return services;
    }

    private static void AddTelegramLongPollingReceivingCore(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient>? httpClientFactory,
        params Assembly[] assemblies
    )
    {
        services.AddTelegramBot<IOptions<TelegramReceiverConfiguration>>(
            (bot, receiver) => bot.Token = receiver.Value.Token,
            httpClientFactory
        );
        services.AddTelegramReceiving(assemblies);
        services.AddSingleton<ScopedUpdateHandler>();
        services.AddHostedService<TelegramLongPollingInitializer>();
        services.AddHostedService<TelegramCommandMenuInitializer<TelegramReceiverConfiguration>>();
        services.AddHostedService<TelegramLongPollingBackgroundService>();
    }
}
