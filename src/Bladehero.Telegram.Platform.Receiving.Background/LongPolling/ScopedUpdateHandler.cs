using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

// Telegram.Bot's ReceiveAsync returns on any OperationCanceledException from a handler and stops if HandleErrorAsync
// throws, so nothing but the shutdown's own cancellation leaves here: every other failure goes to the error handler.
internal sealed class ScopedUpdateHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ScopedUpdateHandler> logger,
    TimeProvider? timeProvider = null
) : IUpdateHandler
{
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QuietSpell = TimeSpan.FromMinutes(1);

    // The app's clock when it registered one, such as a test's FakeTimeProvider; the library registers none.
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private TimeSpan _nextWait = FirstWait;
    private DateTimeOffset? _lastPollingError;

    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IUpdateHandler>();
            await handler.HandleUpdateAsync(botClient, update, cancellationToken);
        }
        catch (Exception exception) when (!IsShutdown(exception, cancellationToken))
        {
            await ReportAsync(new TelegramError(exception, botClient, update), cancellationToken);
        }
        finally
        {
            ResetWait();
        }
    }

    public async Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken
    )
    {
        await ReportAsync(new TelegramError(exception, botClient), cancellationToken);

        if (source == HandleErrorSource.PollingError)
        {
            // Telegram.Bot's loop polls again at once, so an outage would otherwise spin; shutdown ends the wait.
            await Task.Delay(NextWait(), _time, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task ReportAsync(TelegramError error, CancellationToken cancellationToken)
    {
        using var logScope = UpdateLogScope.Begin(logger, error.Update);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var errorHandler = scope.ServiceProvider.GetRequiredService<ITelegramErrorHandler>();
            await errorHandler.HandleAsync(error);
        }
        catch (Exception failure) when (!IsShutdown(failure, cancellationToken))
        {
            // Both stacks: the error first, then the handler's failure.
            logger.LogError(
                new AggregateException(error.Exception, failure),
                "The Telegram error handler failed on an error from update {UpdateId}",
                error.Update?.Id
            );
        }
    }

    // 1 s, doubling up to 30 s while polls keep failing; from 1 s again after an update or a quiet minute.
    private TimeSpan NextWait()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _lastPollingError > QuietSpell)
            {
                _nextWait = FirstWait;
            }

            _lastPollingError = now;
            var wait = _nextWait;
            _nextWait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, LongestWait.Ticks));
            return wait;
        }
    }

    private void ResetWait()
    {
        lock (_gate)
        {
            _nextWait = FirstWait;
            _lastPollingError = null;
        }
    }

    private static bool IsShutdown(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
