using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot.Polling;

namespace Bladehero.Telegram.Platform.Receiving;

/// <summary>
/// Registers the receiving services; <c>AddTelegramLongPollingReceiving</c> and <c>AddTelegramWebhookReceiving</c> call
/// it for you.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the commands found in <paramref name="assemblies"/>, their execution, conversations, the command menu,
    /// typed buttons and the default error handler; the hosting packages call it, so an app rarely does.
    /// </summary>
    /// <remarks>
    /// A command is any class, public or not, that implements <see cref="ITelegramCommand"/> and is neither abstract
    /// nor generic; each is registered as scoped.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="assemblies"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The typed buttons can't be set up: a <see cref="ButtonAttribute"/> struct holds fields a button can't, two
    /// share a prefix, a button type has more than one command (or step) for it, or a command for a type without
    /// <see cref="ButtonAttribute"/> doesn't override <c>Parse</c>. The message lists every problem.
    /// </exception>
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
        services.TryAddScoped<IButtonRefusalHandler, DefaultButtonRefusalHandler>();
        services.AddScoped<ParallelTelegramCommandExecutor>();
        services.AddScoped<ITelegramCommandExecutor, ConversationAwareCommandExecutor>();
        services.AddScoped<IUpdateHandler, ReceivingUpdateHandler>();
        services.AddTelegramConversations();
        services.AddTelegramCommands(assemblies);
        return services;
    }
}
