using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Errors;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

internal sealed class ScopeLog
{
    public List<ScopedDependency> Created { get; } = [];
    public List<ScopedDependency> Disposed { get; } = [];
    public List<ScopedDependency> SeenByUpdates { get; } = [];
    public List<ScopedDependency> SeenByErrors { get; } = [];
    public List<ProbeCommand> CommandInstances { get; } = [];
}

internal sealed class ScopedDependency : IDisposable
{
    private readonly ScopeLog _log;

    public ScopedDependency(ScopeLog log)
    {
        _log = log;
        _log.Created.Add(this);
    }

    public void Dispose() => _log.Disposed.Add(this);
}

internal sealed class ProbeCommand(ScopeLog log, ScopedDependency dependency) : ITelegramCommand
{
    public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token)
    {
        log.CommandInstances.Add(this);
        return Task.FromResult(true);
    }

    public Task HandleAsync(CommandRequest request, CancellationToken token)
    {
        log.SeenByUpdates.Add(dependency);
        return Task.CompletedTask;
    }
}

internal sealed class ProbeErrorHandler(ScopeLog log, ScopedDependency dependency) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        log.SeenByErrors.Add(dependency);
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingErrorHandler(ScopeLog log, ScopedDependency dependency) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        log.SeenByErrors.Add(dependency);
        throw new InvalidOperationException("boom");
    }
}
