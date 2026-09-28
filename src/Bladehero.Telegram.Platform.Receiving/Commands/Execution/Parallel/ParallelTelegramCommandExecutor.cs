using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution.Parallel;

internal sealed class ParallelTelegramCommandExecutor(
    CommandPriorityAccessor commandAccessor,
    IOptionsMonitor<ParallelCommandExecutionConfiguration> options
) : ITelegramCommandExecutor
{
    public async Task ExecuteAsync(CommandRequest request, CancellationToken token = default)
    {
        var configuration = options.CurrentValue;
        var chunks = ChunkCommands(configuration.ParallelCount);
        foreach (var chunk in chunks)
        {
            var executables = await GetExecutableCommands(chunk, request, token);
            var tasks = executables.Select(x => x.HandleAsync(request, token));
            await Task.WhenAll(tasks);
        }
    }

    private IEnumerable<IEnumerable<ITelegramCommand>> ChunkCommands(int? chunkSize)
    {
        var groups = commandAccessor.GetGroups();
        return chunkSize.HasValue
            ? groups.Values.Select(x => x.Chunk(chunkSize.Value)).SelectMany(x => x)
            : groups.Values;
    }

    private static async Task<IEnumerable<ITelegramCommand>> GetExecutableCommands(
        IEnumerable<ITelegramCommand> chunk,
        CommandRequest request,
        CancellationToken token
    )
    {
        var items = chunk
            .Select(command => new { CanHandleTask = command.CanHandleAsync(request, token), Command = command })
            .ToArray();
        await Task.WhenAll(items.Select(x => x.CanHandleTask));
        return items.Where(x => x.CanHandleTask.Result).Select(x => x.Command);
    }
}
