using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Sandbox.Tests;

public sealed class CoffeeShopTests
{
    [Fact]
    public async Task CoffeeShop_WithoutAMemberResolver_ShouldFailToStartNamingItsCommands()
    {
        // Act
        var act = () =>
            SandboxBot.StartAsync(configure: builder => builder.Services.RemoveAll<ITelegramUserResolver<Member>>());

        // Assert
        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should()
            .ContainAll("ITelegramUserResolver<Member>", "RedeemButton");
    }

    [Fact]
    public async Task CoffeeShop_WithDemoOn_ShouldReadReceipts()
    {
        // Arrange
        await using var bot = await StartWithDemoAsync(("Nick", 0));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("a receipt"u8.ToArray());

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: Receipt: 12.40 EUR → 12 points. [Add 12 points] [Discard]");
    }

    [Fact]
    public async Task CoffeeShop_WithDemoOn_ShouldTakeVoiceOrders()
    {
        // Arrange
        await using var bot = await StartWithDemoAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync("a large coffee, please"u8.ToArray());

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Nick: (voice 1s)",
                "Bot: «A large coffee, please»",
                "Bot: Size: Large ✓",
                "Bot: Whose name goes on the cup? [Cancel]"
            );
    }

    private static Task<TelegramTestHost> StartWithDemoAsync(params (string FirstName, int Points)[] members)
    {
        var api = new FakeBotApi();
        var settings = SandboxBot.Members(api, members);
        settings["CoffeeShop:Demo"] = "true";

        return SandboxBot.StartAsync(api, settings);
    }
}
