using Bladehero.Telegram.Platform.Receiving.Errors;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

// What the test host swaps in the app, in either receiving mode.
public sealed class HostingTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Update_WhenTheAppRegistersItsOwnBotClient_ShouldTalkToTheFake(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web =>
                web.ConfigureTestServices(services =>
                    services.AddSingleton<ITelegramBotClient>(
                        new TelegramBotClient("7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw")
                    )
                )
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");
        await bot.Services.GetRequiredService<ITelegramBotClient>().SendMessage(nick.Chat.Id, "Your limit is near");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal("Nick: hello", "Bot: Reply: hello [Again]", "Bot: Your limit is near");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Error_ShouldReachTheAppsOwnErrorHandlerWithItsUpdate(BotMode mode)
    {
        // Arrange
        var seen = new SeenErrors();
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web =>
                web.ConfigureTestServices(services =>
                {
                    services.AddSingleton(seen);
                    services.AddScoped<ITelegramErrorHandler, SeenErrorsHandler>();
                })
        );
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(403);
            var error = seen.All.Should().ContainSingle().Subject;
            error.Exception.Should().BeSameAs(thrown);
            error.Update!.Id.Should().Be(1);
            error.Update.Message!.Chat.Id.Should().Be(nick.Chat.Id);
        }
    }

    // The app's own error handler, and what it saw.
    private sealed class SeenErrors
    {
        private readonly List<TelegramError> _all = [];

        public IReadOnlyList<TelegramError> All
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Add(TelegramError error)
        {
            lock (_all)
            {
                _all.Add(error);
            }
        }
    }

    private sealed class SeenErrorsHandler(SeenErrors seen) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            seen.Add(telegramError);
            return Task.CompletedTask;
        }
    }
}
