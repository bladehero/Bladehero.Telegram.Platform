using Bladehero.Telegram.Platform.Receiving.Commands.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Commands;

/// <summary>
/// A command: a class found by assembly scanning that says whether it handles an update, then handles it.
/// </summary>
/// <remarks>
/// Commands are scoped to the update and not exclusive: every command whose <see cref="CanHandleAsync"/> returns
/// <c>true</c> runs. Derive from a typed base such as <c>MessageCommand</c> to get a typed payload.
/// </remarks>
public interface ITelegramCommand
{
    /// <summary>
    /// Whether this command handles the update; every command that returns <c>true</c> runs, as commands are not
    /// exclusive.
    /// </summary>
    Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token);

    /// <summary>
    /// Handles the update once <see cref="CanHandleAsync"/> has returned <c>true</c>, on the same instance.
    /// </summary>
    Task HandleAsync(CommandRequest request, CancellationToken token);
}
