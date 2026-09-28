using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Polling;

namespace Bladehero.Telegram.Platform.Receiving;

public static class DependencyInjection
{
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
        services.AddScoped<ITelegramCommandExecutor, ParallelTelegramCommandExecutor>();
        services.AddScoped<IUpdateHandler, ReceivingUpdateHandler>();
        services.AddTelegramCommands(assemblies);
        return services;
    }
}
