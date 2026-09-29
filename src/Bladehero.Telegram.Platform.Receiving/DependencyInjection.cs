using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Polling;

namespace Bladehero.Telegram.Platform.Receiving;

/// <summary>
/// Registers the receiving services; <c>AddTelegramLongPollingReceiving</c> and <c>AddTelegramWebhookReceiving</c> call
/// it for you.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the commands found in <paramref name="assemblies"/>, their execution, conversations, the command menu
    /// and the default error handler; the hosting packages call it, so an app rarely does.
    /// </summary>
    /// <remarks>
    /// A command is any class, public or not, that implements <see cref="ITelegramCommand"/> and is neither abstract
    /// nor generic; each is registered as scoped.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    public static IServiceCollection AddTelegramReceiving(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        if (assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly is required", nameof(assemblies));
        }

        services.AddScoped<ITelegramErrorHandler, LoggingTelegramErrorHandler>();
        services.AddScoped<ParallelTelegramCommandExecutor>();
        services.AddScoped<ITelegramCommandExecutor, ConversationAwareCommandExecutor>();
        services.AddScoped<IUpdateHandler, ReceivingUpdateHandler>();
        services.AddTelegramConversations();
        services.AddTelegramCommands(assemblies);
        return services;
    }
}
