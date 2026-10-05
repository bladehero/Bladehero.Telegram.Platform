using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

internal sealed class ScopeLog
{
    public List<ScopedDependency> Created { get; } = [];
    public List<ScopedDependency> Disposed { get; } = [];
    public List<ScopedDependency> SeenByUpdates { get; } = [];
    public List<ScopedDependency> SeenByErrors { get; } = [];
    public List<ProbeCommand> CommandInstances { get; } = [];
    public List<TelegramError> Errors { get; } = [];

    /// <summary>What the command throws, if anything.</summary>
    public Exception? CommandFailure { get; set; }

    /// <summary>What the command replies to the update's chat, if anything.</summary>
    public string? Reply { get; set; }

    /// <summary>What <see cref="ProbeErrorHandler"/> tells the failed update's chat, if anything.</summary>
    public string? Apology { get; set; }

    /// <summary>Gates the command stops at before handling an update, by update id.</summary>
    public ConcurrentDictionary<int, UpdateGate> Gates { get; } = new();
}

/// <summary>Holds the command at the start of one update until opened, and says when it got there.</summary>
internal sealed class UpdateGate
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Reached => _reached.Task;

    public void Open() => _opened.TrySetResult();

    public Task PassAsync()
    {
        _reached.TrySetResult();
        return _opened.Task;
    }
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

    public async Task HandleAsync(CommandRequest request, CancellationToken token)
    {
        if (log.Gates.TryGetValue(request.Update.Id, out var gate))
        {
            await gate.PassAsync();
        }

        log.SeenByUpdates.Add(dependency);
        if (log.CommandFailure is { } failure)
        {
            throw failure;
        }

        if (log.Reply is { } reply)
        {
            await request.Client.SendMessage(request.Update.Message!.Chat, reply, cancellationToken: token);
        }
    }
}

internal sealed class ProbeErrorHandler(ScopeLog log, ScopedDependency dependency) : ITelegramErrorHandler
{
    public Task HandleAsync(TelegramError telegramError)
    {
        log.SeenByErrors.Add(dependency);
        log.Errors.Add(telegramError);
        return log.Apology is { } apology && telegramError.Update?.Message is { } message
            ? telegramError.Client.SendMessage(message.Chat, apology)
            : Task.CompletedTask;
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
