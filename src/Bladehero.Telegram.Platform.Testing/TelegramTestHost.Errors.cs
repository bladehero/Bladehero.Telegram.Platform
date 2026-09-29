using System.Runtime.ExceptionServices;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;

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

        public void Add(long? updateId, Exception exception)
        {
            lock (_errors)
            {
                if (updateId is not { } id || !_over.Contains(id))
                {
                    _errors.Add(new Recorded(updateId, exception));
                }
            }
        }

        // Rethrows the first error of the update, else the first with no update; the update's errors are forgotten.
        public void ThrowFor(long? updateId)
        {
            Exception? first;
            lock (_errors)
            {
                first = updateId is { } id ? Forget(id) : null;
                first ??= Forget(x => x.UpdateId is null);
            }

            if (first is not null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }

        public void Abandon(long updateId)
        {
            lock (_errors)
            {
                Forget(updateId);
            }
        }

        private Exception? Forget(long updateId)
        {
            _over.Add(updateId);
            return Forget(x => x.UpdateId == updateId);
        }

        private Exception? Forget(Predicate<Recorded> match)
        {
            var first = _errors.Find(match)?.Exception;
            _errors.RemoveAll(match);
            return first;
        }

        private sealed record Recorded(long? UpdateId, Exception Exception);
    }

    // Records each error for the action that caused it, then hands it to the app's own handler in the same scope.
    private sealed class RecordingErrorHandler(ErrorLog errors, IServiceProvider services) : ITelegramErrorHandler
    {
        public async Task HandleAsync(TelegramError telegramError)
        {
            var updateId = telegramError.Update?.Id;
            errors.Add(updateId, telegramError.Exception);

            try
            {
                if (services.GetKeyedService<ITelegramErrorHandler>(AppsErrorHandler) is { } appsOwn)
                {
                    await appsOwn.HandleAsync(telegramError);
                }
            }
            catch (Exception exception)
            {
                errors.Add(updateId, exception);
            }
        }
    }
}
