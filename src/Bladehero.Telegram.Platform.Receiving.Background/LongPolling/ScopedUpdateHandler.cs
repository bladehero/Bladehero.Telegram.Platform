using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.LongPolling;

internal sealed class ScopedUpdateHandler(IServiceScopeFactory scopeFactory) : IUpdateHandler
{
    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IUpdateHandler>();
        await handler.HandleUpdateAsync(botClient, update, cancellationToken);
    }

    public async Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IUpdateHandler>();
        await handler.HandleErrorAsync(botClient, exception, source, cancellationToken);
    }
}
