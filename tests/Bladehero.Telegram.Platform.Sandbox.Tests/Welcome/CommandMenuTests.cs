using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Welcome;

public sealed class CommandMenuTests
{
    private static readonly string[] Menu =
    [
        "/start Start over",
        "/coffee Order a coffee",
        "/cancel Cancel the current order",
        "/help What I can do",
        "/history Your receipts as a CSV file",
        "/join Join the loyalty club",
        "/leave Leave the loyalty club",
        "/points Your loyalty points",
        "/redeem Redeem points, e.g. /redeem 10",
    ];

    [Fact]
    public async Task CommandMenu_ShouldListStartAndCoffeeFirstThenTheRestAlphabetically()
    {
        // Act
        await using var bot = await SandboxBot.StartAsync();

        // Assert
        Shown(bot.Api.CommandMenu()).Should().Equal(Menu);
    }

    [Fact]
    public async Task CommandMenu_WithThePrivateChatsScope_ShouldBePublishedOnlyThere()
    {
        // Act
        await using var bot = await SandboxBot.StartAsync(
            settings: new Dictionary<string, string?>
            {
                ["TelegramReceiverConfiguration:CommandMenuScope"] = "AllPrivateChats",
            }
        );

        // Assert
        using (new AssertionScope())
        {
            Shown(bot.Api.CommandMenu(new BotCommandScopeAllPrivateChats())).Should().Equal(Menu);
            bot.Api.CommandMenu().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CommandMenu_WhenSyncIsOff_ShouldBeLeftAlone()
    {
        // Act
        await using var bot = await SandboxBot.StartAsync(
            settings: new Dictionary<string, string?> { ["TelegramReceiverConfiguration:SyncCommandMenu"] = "false" }
        );

        // Assert
        bot.Api.Calls.Select(x => x.Method).Should().NotContain(["getMyCommands", "setMyCommands"]);
    }

    [Fact]
    public async Task CommandMenu_AfterARestartOnTheSameFake_ShouldOnlyBeRead()
    {
        // Arrange
        var api = new FakeBotApi();
        await using (await SandboxBot.StartAsync(api)) { }

        var before = api.Calls.Count;

        // Act
        await using var bot = await SandboxBot.StartAsync(api);

        // Assert
        var methods = api.Calls.Skip(before).Select(x => x.Method).ToArray();
        using (new AssertionScope())
        {
            methods.Should().Contain("getMyCommands");
            methods.Should().NotContain("setMyCommands");
        }
    }

    [Fact]
    public async Task CommandMenu_WhenTelegramRefusesIt_ShouldStillServe()
    {
        // Arrange
        var api = new FakeBotApi();
        api.Fail("setMyCommands", new BotApiError(500, "Internal Server Error"));
        await using var bot = await SandboxBot.StartAsync(api);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/start");

        // Assert
        using (new AssertionScope())
        {
            bot.Api.CommandMenu().Should().BeEmpty();
            nick.Messages[1].Text.Should().Be("Welcome to the coffee shop, Nick!");
        }
    }

    private static IEnumerable<string> Shown(IEnumerable<BotCommand> menu) =>
        menu.Select(x => $"/{x.Command} {x.Description}");
}
