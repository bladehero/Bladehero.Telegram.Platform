using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;

internal sealed class ParallelTelegramCommandExecutor(
    CommandPriorityAccessor commandAccessor,
    IOptionsMonitor<ParallelCommandExecutionConfiguration> options
) : ITelegramCommandExecutor
{
    public Task ExecuteAsync(CommandRequest request, CancellationToken token = default) =>
        ExecuteAsync(commandAccessor, request, token);

    internal async Task<bool> ExecuteAsync(
        CommandPriorityAccessor commands,
        CommandRequest request,
        CancellationToken token
    )
    {
        var handled = false;
        foreach (var chunk in ChunkCommands(commands, options.CurrentValue.ParallelCount))
        {
            var executables = await GetExecutableCommands(chunk, request, token);
            handled |= executables.Length > 0;
            await Task.WhenAll(executables.Select(x => x.HandleAsync(request, token)));
        }

        return handled;
    }

    private static IEnumerable<IEnumerable<ITelegramCommand>> ChunkCommands(
        CommandPriorityAccessor commands,
        int? chunkSize
    )
    {
        var groups = commands.GetGroups();
        return chunkSize.HasValue
            ? groups.Values.Select(x => x.Chunk(chunkSize.Value)).SelectMany(x => x)
            : groups.Values;
    }

    private static async Task<ITelegramCommand[]> GetExecutableCommands(
        IEnumerable<ITelegramCommand> chunk,
        CommandRequest request,
        CancellationToken token
    )
    {
        var items = chunk
            .Select(command => new { CanHandleTask = command.CanHandleAsync(request, token), Command = command })
            .ToArray();
        await Task.WhenAll(items.Select(x => x.CanHandleTask));
        return [.. items.Where(x => x.CanHandleTask.Result).Select(x => x.Command)];
    }
}
