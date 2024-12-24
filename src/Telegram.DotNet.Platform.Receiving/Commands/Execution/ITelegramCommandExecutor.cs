namespace Telegram.DotNet.Platform.Receiving.Commands.Execution;

public interface ITelegramCommandExecutor
{
    Task ExecuteAsync(CommandRequest request, CancellationToken token = default);
}
