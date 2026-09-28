using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

public sealed class TelegramCommandMenuInitializerTests
{
    private static readonly BotCommand Start = new() { Command = "start", Description = "start over" };
    private static readonly BotCommand Help = new() { Command = "help", Description = "what I can do" };

    [Fact]
    public async Task WithNoCommandsDeclaredTelegramIsNotAsked()
    {
        var client = new FakeBotClient();

        await InitializerFor(client, declared: []).StartingAsync(CancellationToken.None);

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task WithSyncTurnedOffTelegramIsNotAsked()
    {
        var client = new FakeBotClient();

        await InitializerFor(client, sync: false).StartingAsync(CancellationToken.None);

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task AMenuTelegramAlreadyShowsIsLeftAsItIs()
    {
        var client = new FakeBotClient(menu: [Start, Help]);

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getMyCommands"], client.Requests);
    }

    [Fact]
    public async Task AChangedMenuIsSentInItsDeclaredOrder()
    {
        var client = new FakeBotClient(menu: [Start]);

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getMyCommands", "setMyCommands"], client.Requests);
        Assert.Equal(["start", "help"], client.SentMenu!.Select(command => command.Command));
    }

    [Fact]
    public async Task AReorderedMenuIsSentAgain()
    {
        var client = new FakeBotClient(menu: [Help, Start]);

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.Equal(["getMyCommands", "setMyCommands"], client.Requests);
    }

    [Fact]
    public async Task ByDefaultTheMenuGoesToEveryChat()
    {
        var client = new FakeBotClient();

        await InitializerFor(client).StartingAsync(CancellationToken.None);

        Assert.All(client.MenuScopes, scope => Assert.IsType<BotCommandScopeDefault>(scope));
    }

    [Fact]
    public async Task TheMenuIsReadAndWrittenInTheConfiguredScope()
    {
        var client = new FakeBotClient();

        await InitializerFor(client, scope: CommandMenuScope.AllPrivateChats).StartingAsync(CancellationToken.None);

        Assert.Equal(2, client.MenuScopes.Count);
        Assert.All(client.MenuScopes, scope => Assert.IsType<BotCommandScopeAllPrivateChats>(scope));
    }

    [Fact]
    public async Task AFailureToReachTelegramDoesNotStopTheHostFromStarting()
    {
        var client = new FakeBotClient(unreachable: true);

        var exception = await Record.ExceptionAsync(() => InitializerFor(client).StartingAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    private static TelegramCommandMenuInitializer<TelegramReceiverConfiguration> InitializerFor(
        ITelegramBotClient client,
        BotCommand[]? declared = null,
        bool sync = true,
        CommandMenuScope scope = CommandMenuScope.Default
    ) =>
        new(
            new TelegramBotClientAccessor(client),
            new Menu(declared ?? [Start, Help]),
            Options.Create(
                new TelegramReceiverConfiguration
                {
                    Token = "unused",
                    SyncCommandMenu = sync,
                    CommandMenuScope = scope,
                }
            ),
            NullLogger<TelegramCommandMenuInitializer<TelegramReceiverConfiguration>>.Instance
        );

    private sealed class Menu(IReadOnlyList<BotCommand> commands) : IBotCommandMenu
    {
        public IReadOnlyList<BotCommand> Commands => commands;
    }
}
