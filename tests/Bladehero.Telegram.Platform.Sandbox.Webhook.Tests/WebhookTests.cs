using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

public sealed class WebhookTests
{
    [Fact]
    public async Task Startup_ShouldSetTheWebhookToTheConfiguredAddress()
    {
        // Act
        await using var bot = await WebhookBot.StartAsync();

        // Assert
        bot.Api.WebhookUrl.Should().Be($"{WebhookBot.BaseUrl}/telegram/updates");
    }

    [Fact]
    public async Task Startup_ShouldCheckTheWebhookBeforeSettingIt()
    {
        // Act
        await using var bot = await WebhookBot.StartAsync();

        // Assert
        bot.Api.Calls.Select(x => x.Method).Should().ContainInOrder("getMe", "getWebhookInfo", "setWebhook");
    }

    [Fact]
    public async Task Update_WhenTelegramRefusedTheWebhook_ShouldSayThereIsNowhereToPostIt()
    {
        // Arrange — Telegram only delivers over HTTPS, so it refuses this webhook as the bot starts.
        await using var bot = await WebhookBot.StartAsync(baseUrl: "http://bot.example.com");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*set no webhook*");
    }

    [Fact]
    public async Task Update_ShouldCarryTheSecretTokenTheBotSet()
    {
        // Arrange
        var headers = new SecretTokenHeaders();
        await using var bot = await WebhookBot.StartAsync(configure: web =>
            web.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(headers))
        );
        await bot.Api.CreateClient().SetWebhook(bot.Api.WebhookUrl!, secretToken: "s3cret");

        // Act
        await bot.PrivateChat("Nick").SendsAsync("hello");

        // Assert
        headers.Seen.Should().Equal("s3cret");
    }

    [Fact]
    public async Task Update_PostedWhereNoEndpointListens_ShouldSayTheBotRefusedIt()
    {
        // Arrange
        await using var bot = await WebhookBot.StartAsync();
        await bot.Api.CreateClient().SetWebhook($"{WebhookBot.BaseUrl}/elsewhere");
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync("hello");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*/elsewhere with 404*");
    }

    // Notes the secret token header on every request the app receives.
    private sealed class SecretTokenHeaders : IStartupFilter
    {
        public List<string> Seen { get; } = [];

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    async (context, proceed) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var value))
                        {
                            Seen.Add(value.ToString());
                        }

                        await proceed();
                    }
                );
                next(app);
            };
    }
}
