using System.Runtime.ExceptionServices;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Testing;

// The errors the bot raises, kept for the action that caused them.
public sealed partial class TelegramTestHost
{
    // Errors by the update that raised them; a null update is a failed poll.
    private sealed class ErrorLog
    {
        private readonly List<Recorded> _errors = [];

        // Updates whose action is over: it returned, or the test stopped waiting for it.
        private readonly HashSet<long> _over = [];

        // Every error ever recorded, so its log isn't reported as a logged error too.
        private readonly HashSet<Exception> _recorded = new(ReferenceEqualityComparer.Instance);

        public TestLogProvider Logs { get; } = new();

        public void Add(long? updateId, Exception exception)
        {
            lock (_errors)
            {
                _recorded.Add(exception);
                if (updateId is not { } id || !_over.Contains(id))
                {
                    _errors.Add(new Recorded(updateId, exception));
                }
            }
        }

        // The app's own handler threw while handling `exception`.
        public void AddHandlerFailure(Exception exception, Exception failure)
        {
            lock (_errors)
            {
                if (_errors.Find(x => x.Exception == exception) is { } recorded)
                {
                    recorded.HandlerFailure ??= failure;
                }
            }
        }

        // Rethrows the first error of the update, else the first with no update; the update's errors are forgotten.
        public void ThrowFor(long? updateId)
        {
            Recorded? first;
            lock (_errors)
            {
                first = updateId is { } id ? Forget(id) : null;
                first ??= Forget(x => x.UpdateId is null);
            }

            // The action fails with the error itself, so what its update logged isn't reported later.
            if (first?.UpdateId is { } failed)
            {
                Logs.Claim(x => x.UpdateId == failed && x.Level is LogLevel.Error or LogLevel.Critical);
            }

            if (first is { HandlerFailure: { } failure })
            {
                var whose = first.UpdateId is { } id ? $"update {id}'s error" : "an error with no update";
                throw new AggregateException(
                    $"The app's ITelegramErrorHandler failed while handling {whose}.",
                    first.Exception,
                    failure
                );
            }

            if (first is not null)
            {
                ExceptionDispatchInfo.Capture(first.Exception).Throw();
            }
        }

        // Throws for the unclaimed Error and Critical entries of the update, of none, or of one whose action is over,
        // other than recorded errors.
        public void ThrowForLoggedErrors(long? updateId)
        {
            var logged = Logs.Claim(x =>
                x.Level is LogLevel.Error or LogLevel.Critical
                && (x.UpdateId is null || x.UpdateId == updateId || IsOver(x.UpdateId.Value))
                && !WasRecorded(x.Exception)
            );

            if (logged is not [var first, ..])
            {
                return;
            }

            var where = first.UpdateId is { } id ? $"while handling update {id}" : "outside any update";
            var more = logged.Count > 1 ? $" (and {logged.Count - 1} more)" : "";
            throw new InvalidOperationException($"The bot logged an error {where}: {first}{more}", first.Exception);
        }

        public void Abandon(long updateId)
        {
            lock (_errors)
            {
                Forget(updateId);
            }
        }

        private bool IsOver(long updateId)
        {
            lock (_errors)
            {
                return _over.Contains(updateId);
            }
        }

        // Also an aggregate of one, as the library logs when an error handler fails.
        private bool WasRecorded(Exception? exception)
        {
            lock (_errors)
            {
                return exception is not null
                    && (
                        _recorded.Contains(exception)
                        || exception is AggregateException aggregate
                            && aggregate.InnerExceptions.Any(_recorded.Contains)
                    );
            }
        }

        private Recorded? Forget(long updateId)
        {
            _over.Add(updateId);
            return Forget(x => x.UpdateId == updateId);
        }

        private Recorded? Forget(Predicate<Recorded> match)
        {
            var first = _errors.Find(match);
            _errors.RemoveAll(match);
            return first;
        }

        private sealed record Recorded(long? UpdateId, Exception Exception)
        {
            public Exception? HandlerFailure { get; set; }
        }
    }

    // Records each error for the action that caused it, then hands it to the app's own handler in the same scope.
    private sealed class RecordingErrorHandler(ErrorLog errors, IServiceProvider services) : ITelegramErrorHandler
    {
        public async Task HandleAsync(TelegramError telegramError)
        {
            errors.Add(telegramError.Update?.Id, telegramError.Exception);

            try
            {
                if (services.GetKeyedService<ITelegramErrorHandler>(AppsErrorHandler) is { } appsOwn)
                {
                    await appsOwn.HandleAsync(telegramError);
                }
            }
            catch (Exception failure)
            {
                errors.AddHandlerFailure(telegramError.Exception, failure);
            }
        }
    }
}
