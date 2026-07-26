using System.Text.Json;
using System.Text.Json.Serialization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.MyChatMembers;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox;

public class LoggingTelegramCommand(ILogger<LoggingTelegramCommand> logger) : MyChatMemberCommand
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    protected override Task<bool> CanHandleAsync(
        TypedCommandRequest<ChatMemberUpdated> request,
        CancellationToken token
    ) => Task.FromResult(true);

    protected override Task HandleAsync(TypedCommandRequest<ChatMemberUpdated> request, CancellationToken token)
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
