using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
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
}
