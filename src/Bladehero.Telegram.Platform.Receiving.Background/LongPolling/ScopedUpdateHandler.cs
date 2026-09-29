using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

// Telegram.Bot's ReceiveAsync returns on any OperationCanceledException from a handler and stops if HandleErrorAsync
// throws, so nothing but the shutdown's own cancellation leaves here: every other failure goes to the error handler.
internal sealed class ScopedUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<ScopedUpdateHandler> logger)
    : IUpdateHandler
{
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
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken
    ) => ReportAsync(new TelegramError(exception, botClient), cancellationToken);

    private async Task ReportAsync(TelegramError error, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var errorHandler = scope.ServiceProvider.GetRequiredService<ITelegramErrorHandler>();
            await errorHandler.HandleAsync(error);
        }
        catch (Exception exception) when (!IsShutdown(exception, cancellationToken))
        {
            logger.LogError(
                exception,
                "The Telegram error handler failed on an error from update {UpdateId}: {Error}",
                error.Update?.Id,
                error.Exception.Message
            );
        }
    }

    private static bool IsShutdown(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
