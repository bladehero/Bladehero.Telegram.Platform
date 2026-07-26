using Bladehero.Telegram.Platform.Receiving.Commands.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

public interface ITelegramCommand
{
    Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token);

    Task HandleAsync(CommandRequest request, CancellationToken token);
}
