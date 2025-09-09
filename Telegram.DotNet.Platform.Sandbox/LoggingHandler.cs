using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.DotNet.Platform.Receiving.Commands;
using Telegram.DotNet.Platform.Receiving.Commands.Execution;
using Telegram.DotNet.Platform.Receiving.Commands.Typed;
using Telegram.DotNet.Platform.Receiving.Commands.Typed.MyChatMembers;

namespace Telegram.DotNet.Platform.Sandbox;

public class LoggingTelegramCommand(ILogger<LoggingTelegramCommand> logger) : MyChatMemberCommand
{
    private static readonly JsonSerializerOptions Options =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };

    protected override Task<bool> CanHandleAsync(
        TypedCommandRequest<ChatMemberUpdated> request,
        CancellationToken token = default
    ) => Task.FromResult(true);

    protected override Task HandleAsync(
        TypedCommandRequest<ChatMemberUpdated> request,
        CancellationToken token = default
    )
    {
        logger.LogInformation(
            """
            Telegram bot request received:
            {Update}
            """,
            JsonSerializer.Serialize(request.Payload, Options)
        );
        return Task.CompletedTask;
    }
}
