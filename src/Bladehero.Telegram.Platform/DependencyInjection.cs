using Bladehero.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform;

/// <summary>
/// Registers the bot's <see cref="ITelegramBotClient"/> and <see cref="ITelegramSender"/>, for an app that only sends;
/// the receiving setups call it themselves.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the bot's <see cref="ITelegramBotClient"/> and <see cref="ITelegramSender"/>, with
    /// <see cref="TelegramBotConfiguration"/> bound from <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// The client is added only if none is registered, so an <see cref="ITelegramBotClient"/> the app registers itself,
    /// before or after, is the one the library uses too.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configuration">The configuration that holds the bot's section.</param>
    /// <param name="sectionName">
    /// The section to bind; when <c>null</c>, the one named after the type: <c>TelegramBotConfiguration</c>.
    /// </param>
    /// <param name="httpClientFactory">
    /// Builds the <see cref="HttpClient"/> of the client the library builds, e.g. for a proxy; it doesn't apply to a
    /// client the app registers itself.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
    {
        services.AddConfiguration<TelegramBotConfiguration>(configuration, sectionName);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot's <see cref="ITelegramBotClient"/> and <see cref="ITelegramSender"/>, with
    /// <see cref="TelegramBotConfiguration"/> set by <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// The client is added only if none is registered, so an <see cref="ITelegramBotClient"/> the app registers itself,
    /// before or after, is the one the library uses too.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Sets the configuration, e.g. its token.</param>
    /// <param name="httpClientFactory">
    /// Builds the <see cref="HttpClient"/> of the client the library builds, e.g. for a proxy; it doesn't apply to a
    /// client the app registers itself.
    /// </param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        Action<TelegramBotConfiguration> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot like <see cref="AddTelegramBot(IServiceCollection, Action{TelegramBotConfiguration},
    /// Func{IServiceProvider, HttpClient})"/>, with <typeparamref name="TDep1"/> resolved from the container for
    /// <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramBot<TDep1>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot like <see cref="AddTelegramBot(IServiceCollection, Action{TelegramBotConfiguration},
    /// Func{IServiceProvider, HttpClient})"/>, with <typeparamref name="TDep1"/> and <typeparamref name="TDep2"/>
    /// resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramBot<TDep1, TDep2>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot like <see cref="AddTelegramBot(IServiceCollection, Action{TelegramBotConfiguration},
    /// Func{IServiceProvider, HttpClient})"/>, with <typeparamref name="TDep1"/> to <typeparamref name="TDep3"/>
    /// resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot like <see cref="AddTelegramBot(IServiceCollection, Action{TelegramBotConfiguration},
    /// Func{IServiceProvider, HttpClient})"/>, with <typeparamref name="TDep1"/> to <typeparamref name="TDep4"/>
    /// resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    /// <summary>
    /// Registers the bot like <see cref="AddTelegramBot(IServiceCollection, Action{TelegramBotConfiguration},
    /// Func{IServiceProvider, HttpClient})"/>, with <typeparamref name="TDep1"/> to <typeparamref name="TDep5"/>
    /// resolved from the container for <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddTelegramBot<TDep1, TDep2, TDep3, TDep4, TDep5>(
        this IServiceCollection services,
        Action<TelegramBotConfiguration, TDep1, TDep2, TDep3, TDep4, TDep5> configure,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null
    )
        where TDep1 : class
        where TDep2 : class
        where TDep3 : class
        where TDep4 : class
        where TDep5 : class
    {
        services.AddOptions<TelegramBotConfiguration>().Configure(configure);
        services.AddTelegramBotCore(httpClientFactory);
        return services;
    }

    private static void AddTelegramBotCore(
        this IServiceCollection services,
        Func<IServiceProvider, HttpClient>? httpClientFactory
    )
    {
        // One client per bot: a client the app registers itself, before or after, is the one the library uses too.
        services.TryAddSingleton<ITelegramBotClient>(provider =>
        {
            var botConfiguration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
            var options = new TelegramBotClientOptions(botConfiguration.Token);
            return new TelegramBotClient(options, httpClientFactory?.Invoke(provider));
        });
        services.TryAddSingleton<ITelegramSender, TelegramSender>();
    }
}
