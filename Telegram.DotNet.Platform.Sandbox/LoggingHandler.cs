using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Telegram.DotNet.Platform.Receiving.Commands;
using Telegram.DotNet.Platform.Receiving.Commands.Execution;

namespace Telegram.DotNet.Platform.Sandbox;

public class LoggingTelegramCommand(ILogger<LoggingTelegramCommand> logger) : ITelegramCommand
{
    private static readonly JsonSerializerOptions Options =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };

    public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token = default) =>
        Task.FromResult(true);

    public Task HandleAsync(CommandRequest request, CancellationToken token = default)
    {
        logger.LogInformation(
            """
            Telegram bot request received:
            {Update}
            """,
            JsonSerializer.Serialize(request.Update, Options)
        );
        return Task.CompletedTask;
    }
}
