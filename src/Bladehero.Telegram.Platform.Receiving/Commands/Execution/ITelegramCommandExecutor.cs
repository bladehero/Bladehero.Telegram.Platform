namespace Bladehero.Telegram.Platform.Receiving.Commands.Execution;

/// <summary>
/// Dispatches each update to the commands; the default runs the active conversation's steps first, then the regular
/// commands by priority if every step declines, and answers a typed button's tap that nothing took.
/// </summary>
/// <remarks>
/// Registering your own after the receiving services replaces dispatch entirely, conversation routing and those
/// answers included.
/// </remarks>
public interface ITelegramCommandExecutor
{
    /// <summary>
    /// Runs the commands that accept the update in <paramref name="request"/>, within the update's scope.
    /// </summary>
    Task ExecuteAsync(CommandRequest request, CancellationToken token = default);
}
