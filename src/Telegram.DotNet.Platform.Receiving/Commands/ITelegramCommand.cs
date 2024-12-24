using Telegram.DotNet.Platform.Receiving.Commands.Execution;

namespace Telegram.DotNet.Platform.Receiving.Commands;

public interface ITelegramCommand
{
    Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token = default);

    Task HandleAsync(CommandRequest request, CancellationToken token = default);
}
