using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background;

internal sealed class TelegramCommandMenuInitializer<TConfiguration>(
    TelegramBotClientAccessor accessor,
    IBotCommandMenu menu,
    IOptions<TConfiguration> options,
    ILogger<TelegramCommandMenuInitializer<TConfiguration>> logger
) : IHostedLifecycleService
    where TConfiguration : TelegramReceiverConfiguration
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (menu.Commands.Count == 0)
        {
            return;
        }

        var configuration = options.Value;
        if (!configuration.SyncCommandMenu)
        {
            logger.LogInformation("Command menu sync is turned off, so the menu Telegram shows is left as it is");
            return;
        }

        try
        {
            var scope = ScopeOf(configuration.CommandMenuScope);
            var shown = await accessor.Client.GetMyCommands(scope, cancellationToken: cancellationToken);

            if (Matches(shown))
            {
                logger.LogInformation("Command menu is up to date: {Count} commands", shown.Length);
                return;
            }

            await accessor.Client.SetMyCommands(menu.Commands, scope, cancellationToken: cancellationToken);
            logger.LogInformation("Command menu updated: {Count} commands", menu.Commands.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update the command menu");
        }
    }

    private bool Matches(IReadOnlyList<BotCommand> shown) =>
        shown
            .Select(x => (x.Command, x.Description))
            .SequenceEqual(menu.Commands.Select(x => (x.Command, x.Description)));

    private static BotCommandScope ScopeOf(CommandMenuScope scope) =>
        scope switch
        {
            CommandMenuScope.Default => new BotCommandScopeDefault(),
            CommandMenuScope.AllPrivateChats => new BotCommandScopeAllPrivateChats(),
            CommandMenuScope.AllGroupChats => new BotCommandScopeAllGroupChats(),
            CommandMenuScope.AllChatAdministrators => new BotCommandScopeAllChatAdministrators(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown command menu scope."),
        };

    #region Ignored

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
